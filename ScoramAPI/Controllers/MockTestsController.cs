using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    [ApiController]
    [Route("api/mocktests")]
    public class MockTestsController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly ITestAttemptService _attemptService;
        private readonly IMyExamScopeService _myExams;

        public MockTestsController(ScoramDbContext db, ITestAttemptService attemptService, IMyExamScopeService myExams)
        {
            _db = db;
            _attemptService = attemptService;
            _myExams = myExams;
        }

        // GET /api/mocktests?examName=&testType=&page=&pageSize=
        // SCORAM_TESTS: now filters to Status == Published only (spec: "Prevent... Accessing
        // unpublished Mock Tests"). IMPORTANT migration note: every MockTest row that existed BEFORE
        // this field was added must be backfilled to Published, or they'll vanish from this list --
        // see the migration instructions.
        [HttpGet]
        public async Task<ActionResult<PagedResult<MockTestSummaryDto>>> List(
            string? examName, [FromQuery] List<Guid>? examIds, string? testType, string? language, int page = 1, int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.MockTests.Where(t => t.Status == TestPublishStatus.Published).AsQueryable();

            // MY EXAMS -- strict scope for a signed-in student: only Mock Tests of their own exams are
            // listed (none at all while My Exams is empty). Matched by the real ExamId FK; a row that
            // predates the FK and was never backfilled (ExamId null) falls back to its legacy
            // ExamName text, so those tests don't silently vanish for the right students. The explicit
            // examName/examIds filters below are applied on top (AND) and can therefore only narrow
            // within the scope. Anonymous visitors/admins aren't scoped -- unchanged.
            var scope = await _myExams.GetScopeAsync(User);
            if (scope.IsScoped)
            {
                var scopedExamIds = scope.ExamIds.ToList();
                var scopedExamNames = scope.ExamNames.ToList();
                query = query.Where(t =>
                    (t.ExamId != null && scopedExamIds.Contains(t.ExamId.Value))
                    || (t.ExamId == null && scopedExamNames.Contains(t.ExamName)));
            }

            // Filter precedence (spec section 37): an explicit single examName -- what every
            // existing caller already sends -- always wins. examIds (plural) is the new "My Exams"
            // default, OR-matched by the real ExamId FK rather than the ExamName string (see
            // MockTest.ExamId's own comment) so a later exam rename can't silently break it. Rows
            // that predate the FK and were never backfilled to a real ExamId (see the
            // AddExamIdToMockTest migration) simply don't match an examIds filter -- same as any
            // other Mock Test whose Exam isn't the one being filtered for.
            if (!string.IsNullOrWhiteSpace(examName))
                query = query.Where(t => t.ExamName == examName);
            else if (examIds is { Count: > 0 })
                query = query.Where(t => t.ExamId != null && examIds.Contains(t.ExamId.Value));
            if (!string.IsNullOrWhiteSpace(testType)) query = query.Where(t => t.TestType.ToString() == testType);
            if (!string.IsNullOrWhiteSpace(language) && Enum.TryParse<PaperLanguage>(language, ignoreCase: true, out var languageFilter))
                query = query.Where(t => t.Language == languageFilter);

            var totalCount = await query.CountAsync();

            var tests = await query
                .OrderByDescending(t => t.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Include(t => t.MockTestQuestions)
                .Include(t => t.Sections)
                .ToListAsync();

            var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
            Dictionary<Guid, int> myAttemptCounts = new();
            HashSet<Guid> bookmarkedIds = new();
            var allTestIds = tests.Select(t => t.Id).ToList();
            if (isAuthenticated && tests.Count > 0)
            {
                var userId = User.GetUserId();
                myAttemptCounts = await _db.StudentTestResults
                    .Where(r => r.UserId == userId && r.MockTestId != null && allTestIds.Contains(r.MockTestId.Value))
                    .GroupBy(r => r.MockTestId!.Value)
                    .Select(g => new { MockTestId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.MockTestId, g => g.Count);

                bookmarkedIds = (await _db.Bookmarks
                    .Where(b => b.UserId == userId && b.MockTestId != null && allTestIds.Contains(b.MockTestId.Value))
                    .Select(b => b.MockTestId!.Value).ToListAsync()).ToHashSet();
            }

            // Total DISTINCT students who've completed each Mock Test on this page -- shown to
            // every visitor regardless of login state (unlike myAttemptCounts above). Same
            // (Id, UserId)-distinct-then-group pattern as StudentPapersController.Browse's own
            // AttemptCount, for the same reason: a student re-attempting the same test shouldn't
            // inflate "Attempted by X students".
            var attemptCounts = tests.Count == 0
                ? new Dictionary<Guid, int>()
                : await _db.StudentTestResults
                    .Where(r => r.MockTestId != null && allTestIds.Contains(r.MockTestId.Value)
                        && (r.Status == TestAttemptStatus.Submitted || r.Status == TestAttemptStatus.AutoSubmitted))
                    .Select(r => new { MockTestId = r.MockTestId!.Value, r.UserId })
                    .Distinct()
                    .GroupBy(r => r.MockTestId)
                    .Select(g => new { MockTestId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.MockTestId, g => g.Count);

            var now = DateTime.UtcNow;
            var items = tests.Select(t => new MockTestSummaryDto
            {
                Id = t.Id,
                Title = t.Title,
                ExamName = t.ExamName,
                ExamId = t.ExamId,
                TestType = t.TestType.ToString(),
                DurationMinutes = t.DurationMinutes,
                NegativeMarkingRatio = t.NegativeMarkingRatio,
                QuestionCount = t.MockTestQuestions.Count,
                Language = t.Language?.ToString(),
                Instructions = t.Instructions,
                ScheduledAt = t.ScheduledAt,
                EndAt = t.EndAt,
                Status = t.Status.ToString(),
                AvailabilityStatus = ComputeAvailability(t, now),
                MaxAttempts = t.MaxAttempts,
                MyAttemptCount = isAuthenticated ? myAttemptCounts.GetValueOrDefault(t.Id, 0) : null,
                AttemptCount = attemptCounts.GetValueOrDefault(t.Id, 0),
                IsBookmarked = bookmarkedIds.Contains(t.Id)
            }).ToList();
            for (var i = 0; i < items.Count; i++) ApplySettingsSummary(items[i], tests[i]);

            return Ok(new PagedResult<MockTestSummaryDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        // GET /api/mocktests/{id}/summary -- same shape as one row of the List above, but by id and
        // without needing to page through the whole list to find it. Exists specifically so the
        // Pre-Exam Instructions screen (see PreExamInstructions.jsx) doesn't have to call GetById
        // just to show Title/Duration/NegativeMarking/QuestionCount/Instructions -- GetById eagerly
        // loads and ships every question's full text+options, which that screen never needs (spec:
        // "Only retrieve the metadata required for the instructions page").
        [HttpGet("{id:guid}/summary")]
        public async Task<ActionResult<MockTestSummaryDto>> GetSummary(Guid id)
        {
            var test = await _db.MockTests.Include(t => t.MockTestQuestions).Include(t => t.Sections).FirstOrDefaultAsync(t => t.Id == id);
            if (test == null) return NotFound();

            var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
            var myAttemptCount = 0;
            var isBookmarked = false;
            if (isAuthenticated)
            {
                var userId = User.GetUserId();
                myAttemptCount = await _db.StudentTestResults.CountAsync(r => r.UserId == userId && r.MockTestId == id);
                isBookmarked = await _db.Bookmarks.AnyAsync(b => b.UserId == userId && b.MockTestId == id);
            }

            var attemptCount = await _db.StudentTestResults
                .Where(r => r.MockTestId == id && (r.Status == TestAttemptStatus.Submitted || r.Status == TestAttemptStatus.AutoSubmitted))
                .Select(r => r.UserId)
                .Distinct()
                .CountAsync();

            var summary = new MockTestSummaryDto
            {
                Id = test.Id,
                Title = test.Title,
                ExamName = test.ExamName,
                ExamId = test.ExamId,
                TestType = test.TestType.ToString(),
                DurationMinutes = test.DurationMinutes,
                NegativeMarkingRatio = test.NegativeMarkingRatio,
                QuestionCount = test.MockTestQuestions.Count,
                Language = test.Language?.ToString(),
                Instructions = test.Instructions,
                ScheduledAt = test.ScheduledAt,
                EndAt = test.EndAt,
                Status = test.Status.ToString(),
                AvailabilityStatus = ComputeAvailability(test, DateTime.UtcNow),
                MaxAttempts = test.MaxAttempts,
                MyAttemptCount = isAuthenticated ? myAttemptCount : null,
                AttemptCount = attemptCount,
                IsBookmarked = isBookmarked
            };
            ApplySettingsSummary(summary, test);
            return Ok(summary);
        }

        // GET /api/mocktests/{id}
        // Returns questions WITHOUT the answer key -- see MockTestQuestionDto.
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<MockTestDetailDto>> GetById(Guid id)
        {
            var test = await _db.MockTests
                .Include(t => t.MockTestQuestions)
                    .ThenInclude(mq => mq.Question)
                .Include(t => t.MockTestQuestions)
                    .ThenInclude(mq => mq.QuestionBankQuestion)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (test == null) return NotFound();

            var orderedQuestions = test.MockTestQuestions.OrderBy(mq => mq.QuestionOrder).ToList();

            // Simple randomization: shuffle question order per request when IsRandomOrder
            // is set. NOTE: this doesn't persist a fixed order per attempt (reloading the
            // page would re-shuffle) -- fine for this scope, flagged here for later.
            if (test.IsRandomOrder)
            {
                var rng = new Random();
                orderedQuestions = orderedQuestions.OrderBy(_ => rng.Next()).ToList();
            }

            return Ok(new MockTestDetailDto
            {
                Id = test.Id,
                Title = test.Title,
                ExamName = test.ExamName,
                ExamId = test.ExamId,
                TestType = test.TestType.ToString(),
                DurationMinutes = test.DurationMinutes,
                NegativeMarkingRatio = test.NegativeMarkingRatio,
                IsRandomOrder = test.IsRandomOrder,
                // SCORAM_TESTS: a MockTestQuestion can now come from either the legacy Question table
                // or the Question Bank (mq.QuestionId is nullable) -- QuestionId here is just this old
                // DTO's identifier field, so it falls back to the Question Bank id when that's the
                // source; the display fields fall back the same way.
                Questions = orderedQuestions.Select(mq => new MockTestQuestionDto
                {
                    QuestionId = mq.QuestionId ?? mq.QuestionBankQuestionId ?? Guid.Empty,
                    QuestionOrder = mq.QuestionOrder,
                    QuestionText = mq.Question?.QuestionText ?? mq.QuestionBankQuestion?.QuestionText ?? string.Empty,
                    OptionA = mq.Question?.OptionA ?? mq.QuestionBankQuestion?.OptionA ?? string.Empty,
                    OptionB = mq.Question?.OptionB ?? mq.QuestionBankQuestion?.OptionB ?? string.Empty,
                    OptionC = mq.Question?.OptionC ?? mq.QuestionBankQuestion?.OptionC ?? string.Empty,
                    OptionD = mq.Question?.OptionD ?? mq.QuestionBankQuestion?.OptionD ?? string.Empty
                }).ToList()
            });
        }

        // POST /api/mocktests/{id}/attempts
        // Auto-grades against the stored answer key, persists the attempt +
        // per-question answers, and returns the full breakdown immediately.
        [HttpPost("{id:guid}/attempts")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<MockTestResultDto>> SubmitAttempt(Guid id, MockTestSubmitDto dto)
        {
            var test = await _db.MockTests
                .Include(t => t.MockTestQuestions)
                    .ThenInclude(mq => mq.Question)
                .Include(t => t.MockTestQuestions)
                    .ThenInclude(mq => mq.QuestionBankQuestion)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (test == null) return NotFound(new { message = "Mock test not found." });

            // This old one-shot endpoint grades and returns the full answer key in the same response, so it
            // can't honour a result-release setting (or section timing) -- those tests only run through the
            // start -> auto-save -> submit flow.
            if (test.ResultReleaseMode != ResultReleaseMode.Instant || test.IsSectional)
                return BadRequest(new { message = "This mock test must be taken through the regular test screen." });

            var userId = User.GetUserId();
            // SCORAM_TESTS: keyed by "whichever id represents this question" (legacy or Question
            // Bank) so this old one-shot flow still works for a mixed-source paper, same fallback as
            // GetById above. IsQuestionBank tracks which FK the id actually belongs to, so the
            // StudentAnswer row below can populate the correct one (never both).
            var questionById = test.MockTestQuestions.ToDictionary(
                mq => mq.QuestionId ?? mq.QuestionBankQuestionId ?? Guid.Empty,
                mq => new
                {
                    IsQuestionBank = mq.QuestionId == null,
                    QuestionText = mq.Question?.QuestionText ?? mq.QuestionBankQuestion?.QuestionText ?? string.Empty,
                    OptionA = mq.Question?.OptionA ?? mq.QuestionBankQuestion?.OptionA ?? string.Empty,
                    OptionB = mq.Question?.OptionB ?? mq.QuestionBankQuestion?.OptionB ?? string.Empty,
                    OptionC = mq.Question?.OptionC ?? mq.QuestionBankQuestion?.OptionC ?? string.Empty,
                    OptionD = mq.Question?.OptionD ?? mq.QuestionBankQuestion?.OptionD ?? string.Empty,
                    CorrectOption = mq.Question?.CorrectOption ?? mq.QuestionBankQuestion?.CorrectOption ?? OptionLetter.A,
                    Explanation = mq.Question?.Explanation ?? mq.QuestionBankQuestion?.Explanation
                });
            var answerByQuestion = dto.Answers.ToDictionary(a => a.QuestionId, a => a.SelectedOption);

            int correctCount = 0, wrongCount = 0, skippedCount = 0;
            var studentAnswers = new List<StudentAnswer>();
            var resultQuestions = new List<ResultQuestionDto>();

            foreach (var (questionId, question) in questionById)
            {
                answerByQuestion.TryGetValue(questionId, out var selected);
                var isCorrect = selected.HasValue && selected.Value == question.CorrectOption;

                if (selected == null) skippedCount++;
                else if (isCorrect) correctCount++;
                else wrongCount++;

                studentAnswers.Add(new StudentAnswer
                {
                    QuestionId = question.IsQuestionBank ? null : questionId,
                    QuestionBankQuestionId = question.IsQuestionBank ? questionId : null,
                    // This old one-shot endpoint doesn't do the new snapshot/auto-save flow, but the
                    // snapshot columns are non-nullable strings -- fill them in now so the row is
                    // still valid and "view past attempt" (which now reads snapshots, not a live
                    // join) shows the right thing for attempts submitted through this legacy path too.
                    QuestionTextSnapshot = question.QuestionText,
                    OptionASnapshot = question.OptionA,
                    OptionBSnapshot = question.OptionB,
                    OptionCSnapshot = question.OptionC,
                    OptionDSnapshot = question.OptionD,
                    CorrectOptionSnapshot = question.CorrectOption,
                    ExplanationSnapshot = question.Explanation,
                    SelectedOption = selected,
                    IsCorrect = isCorrect
                });

                resultQuestions.Add(new ResultQuestionDto
                {
                    QuestionId = questionId,
                    QuestionText = question.QuestionText,
                    OptionA = question.OptionA,
                    OptionB = question.OptionB,
                    OptionC = question.OptionC,
                    OptionD = question.OptionD,
                    SelectedOption = selected?.ToString(),
                    CorrectOption = question.CorrectOption.ToString(),
                    IsCorrect = isCorrect,
                    Explanation = question.Explanation
                });
            }

            // Standard exam marking scheme: +1 per correct answer, -NegativeMarkingRatio per
            // wrong answer, 0 for skipped. (Not specified further in the SRS; this matches
            // typical SSC/Railway marking and is the natural fit for the stored ratio field.)
            var score = correctCount - (wrongCount * test.NegativeMarkingRatio);
            var maxPossibleScore = questionById.Count;
            var accuracy = questionById.Count == 0
                ? 0
                : Math.Round((decimal)correctCount / questionById.Count * 100, 2);

            var result = new StudentTestResult
            {
                MockTestId = test.Id,
                UserId = userId,
                // This old one-shot endpoint grades and finishes in a single call -- explicitly mark
                // it Submitted (not left at the model's InProgress default), otherwise the new shared
                // GET /api/tests/attempts/{id} endpoint would treat an already-complete attempt made
                // through this legacy path as still resumable.
                Status = TestAttemptStatus.Submitted,
                Score = score,
                CorrectCount = correctCount,
                WrongCount = wrongCount,
                SkippedCount = skippedCount,
                TimeTakenSeconds = dto.TimeTakenSeconds,
                AttemptedAt = DateTime.UtcNow
            };

            _db.StudentTestResults.Add(result);
            await _db.SaveChangesAsync(); // need result.Id before attaching answers

            foreach (var answer in studentAnswers) answer.StudentTestResultId = result.Id;
            _db.StudentAnswers.AddRange(studentAnswers);
            await _db.SaveChangesAsync();

            return Ok(new MockTestResultDto
            {
                AttemptId = result.Id,
                MockTestId = test.Id,
                MockTestTitle = test.Title,
                Score = score,
                MaxPossibleScore = maxPossibleScore,
                CorrectCount = correctCount,
                WrongCount = wrongCount,
                SkippedCount = skippedCount,
                AccuracyPercent = accuracy,
                TimeTakenSeconds = dto.TimeTakenSeconds,
                AttemptedAt = result.AttemptedAt,
                Questions = resultQuestions
            });
        }

        // GET /api/mocktests/attempts/mine?page=&pageSize=
        // A student's own attempt history -- powers a "Recent Tests" list.
        [HttpGet("attempts/mine")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<PagedResult<AttemptSummaryDto>>> MyAttempts(int page = 1, int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var userId = User.GetUserId();
            // SCORAM_TESTS: this old endpoint is specifically "my Mock Test attempts" (route is under
            // /api/mocktests) -- filtered to TestKind.Mock so a Practice attempt (which has no
            // MockTest at all) never lands here and trips the `r.MockTest!.Title` dereference below.
            var query = _db.StudentTestResults.Where(r => r.UserId == userId && r.MockTestId != null).Include(r => r.MockTest);

            var totalCount = await query.CountAsync();

            // Materialize the raw rows first, then compute accuracy in plain C# --
            // keeps the database query itself simple (no Math.Round/ternary inside the
            // SQL translation) and avoids any provider-translation edge cases.
            var rows = await query
                .OrderByDescending(r => r.AttemptedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var nowUtc = DateTime.UtcNow;
            var items = rows.Select(r =>
            {
                var maxPossible = r.CorrectCount + r.WrongCount + r.SkippedCount;
                var locked = TestResultVisibility.IsLocked(r, nowUtc, out _);
                return new AttemptSummaryDto
                {
                    AttemptId = r.Id,
                    MockTestTitle = r.MockTest!.Title,
                    ExamName = r.MockTest.ExamName,
                    Score = locked ? 0 : r.Score,
                    MaxPossibleScore = maxPossible,
                    AccuracyPercent = locked || maxPossible == 0 ? 0 : Math.Round((decimal)r.CorrectCount / maxPossible * 100, 2),
                    TimeTakenSeconds = r.TimeTakenSeconds,
                    AttemptedAt = r.AttemptedAt
                };
            }).ToList();

            return Ok(new PagedResult<AttemptSummaryDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        // GET /api/mocktests/attempts/{attemptId}
        // Full per-question breakdown of one past attempt -- only visible to the
        // student who took it.
        [HttpGet("attempts/{attemptId:guid}")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<MockTestResultDto>> GetAttempt(Guid attemptId)
        {
            var userId = User.GetUserId();

            var result = await _db.StudentTestResults
                .Include(r => r.MockTest)
                .FirstOrDefaultAsync(r => r.Id == attemptId);

            if (result == null) return NotFound();
            if (result.UserId != userId) return Forbid();
            // SCORAM_TESTS: same "Mock Test attempts only" boundary as GetMyAttempts above -- a
            // Practice attempt has no MockTestId/MockTest at all, so it's simply not found here
            // (it's reachable instead via the new shared GET /api/tests/attempts/{id}).
            if (result.MockTestId == null || result.MockTest == null) return NotFound();
            if (TestResultVisibility.IsLocked(result, DateTime.UtcNow, out var releaseAtUtc))
                return BadRequest(new { message = "The result for this test hasn't been released yet.", resultReleaseAt = releaseAtUtc });

            var answers = await _db.StudentAnswers
                .Where(a => a.StudentTestResultId == attemptId)
                .ToListAsync();

            var maxPossibleScore = result.CorrectCount + result.WrongCount + result.SkippedCount;
            var accuracy = maxPossibleScore == 0 ? 0 : Math.Round((decimal)result.CorrectCount / maxPossibleScore * 100, 2);

            return Ok(new MockTestResultDto
            {
                AttemptId = result.Id,
                MockTestId = result.MockTestId.Value,
                MockTestTitle = result.MockTest.Title,
                Score = result.Score,
                MaxPossibleScore = maxPossibleScore,
                CorrectCount = result.CorrectCount,
                WrongCount = result.WrongCount,
                SkippedCount = result.SkippedCount,
                AccuracyPercent = accuracy,
                TimeTakenSeconds = result.TimeTakenSeconds,
                AttemptedAt = result.AttemptedAt,
                // SCORAM_TESTS: reads from each answer's own frozen snapshot (captured at attempt
                // time) instead of a live Question join -- correct regardless of whether the source
                // was legacy or Question Bank, and immune to a later edit changing what this past
                // attempt shows (the bug this snapshot mechanism exists to close).
                Questions = answers.Select(a => new ResultQuestionDto
                {
                    QuestionId = a.QuestionId ?? a.QuestionBankQuestionId ?? Guid.Empty,
                    QuestionText = a.QuestionTextSnapshot,
                    OptionA = a.OptionASnapshot,
                    OptionB = a.OptionBSnapshot,
                    OptionC = a.OptionCSnapshot,
                    OptionD = a.OptionDSnapshot,
                    SelectedOption = a.SelectedOption?.ToString(),
                    CorrectOption = a.CorrectOptionSnapshot.ToString(),
                    IsCorrect = a.IsCorrect,
                    Explanation = a.ExplanationSnapshot
                }).ToList()
            });
        }

        // POST /api/mocktests  (Admin only) -- kept working exactly as before for QuestionIds-only
        // payloads. QuestionRefs/Status/EndAt/MaxAttempts/Instructions are new, optional additions
        // (SCORAM_TESTS); a caller that only knows the old shape doesn't need to change anything.
        [HttpPost]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<MockTestSummaryDto>> Create(MockTestCreateDto dto)
        {
            var adminId = User.GetAdminId();

            var refs = new List<TestQuestionRefDto>();
            if (dto.QuestionIds != null) refs.AddRange(dto.QuestionIds.Select(id => new TestQuestionRefDto { QuestionId = id }));
            if (dto.QuestionRefs != null) refs.AddRange(dto.QuestionRefs);

            if (refs.Count == 0)
                return BadRequest(new { message = "A mock test needs at least one question." });

            var validationError = await ValidateQuestionRefsAsync(refs);
            if (validationError != null) return BadRequest(new { message = validationError });

            // SECTION-WISE TIMING + RESULT RELEASE (both optional; omitted = behaves exactly as before).
            var sectionInput = (dto.Sections ?? new List<MockTestSectionDto>()).Select(x => (x.Name ?? string.Empty, x.DurationMinutes)).ToList();
            var sectionError = MockTestRules.ValidateSections(sectionInput, out var isSectional, out var sectionTotalMinutes);
            if (sectionError != null) return BadRequest(new { message = sectionError });
            if (sectionInput.Count > 0 && refs.Any(r => r.SectionOrder == null || r.SectionOrder < 1 || r.SectionOrder > sectionInput.Count))
                return BadRequest(new { message = "Every question must be assigned to one of the test's sections." });

            if (!MockTestRules.TryParseReleaseMode(dto.ResultReleaseMode, out var releaseMode))
                return BadRequest(new { message = "Result release must be Instant, AfterDelay, OnDate or AfterWindowEnds." });
            var releaseError = MockTestRules.ValidateResultRelease(releaseMode, dto.ResultDelayMinutes, dto.ResultReleaseAt, dto.EndAt);
            if (releaseError != null) return BadRequest(new { message = releaseError });

            if (!Enum.TryParse<TestPublishStatus>(dto.Status, true, out var status))
                status = TestPublishStatus.Draft;

            // "MY EXAMS" -- when the admin picked a real Exam (dto.ExamId), ExamName is derived from
            // it server-side so the two never drift apart, same as an Exam rename staying in sync
            // with its ChatRoom.Name in ExamsController.Update. A legacy caller that only sends
            // ExamName (no ExamId) keeps working exactly as before -- ExamId just stays null.
            var examName = dto.ExamName;
            Guid? examId = dto.ExamId;
            if (examId.HasValue)
            {
                var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == examId.Value);
                if (exam == null) return BadRequest(new { message = "Selected exam could not be found." });
                examName = exam.Name;
            }

            var test = new MockTest
            {
                Title = dto.Title,
                ExamName = examName,
                ExamId = examId,
                TestType = dto.TestType,
                // A sectional test's total time is the sum of its sections' times.
                DurationMinutes = isSectional ? sectionTotalMinutes : dto.DurationMinutes,
                NegativeMarkingRatio = dto.NegativeMarkingRatio,
                IsRandomOrder = dto.IsRandomOrder,
                IsShuffleOptions = dto.IsShuffleOptions,
                Language = ParseLanguage(dto.Language),
                ScheduledAt = dto.ScheduledAt,
                EndAt = dto.EndAt,
                Status = status,
                MaxAttempts = dto.MaxAttempts,
                Instructions = dto.Instructions,
                IsSectional = isSectional,
                ResultReleaseMode = releaseMode,
                ResultDelayMinutes = releaseMode == ResultReleaseMode.AfterDelay ? dto.ResultDelayMinutes : null,
                ResultReleaseAt = releaseMode == ResultReleaseMode.OnDate ? dto.ResultReleaseAt : null,
                CreatedByAdminId = adminId,
                CreatedAt = DateTime.UtcNow
            };

            _db.MockTests.Add(test);
            await _db.SaveChangesAsync();

            var sections = sectionInput.Select((x, i) => new MockTestSection
            {
                MockTestId = test.Id,
                Name = x.Item1.Trim(),
                SectionOrder = i + 1,
                DurationMinutes = x.DurationMinutes
            }).ToList();
            if (sections.Count > 0) _db.MockTestSections.AddRange(sections);

            var mockTestQuestions = refs.Select((r, index) => new MockTestQuestion
            {
                MockTestId = test.Id,
                QuestionId = r.QuestionId,
                QuestionBankQuestionId = r.QuestionBankQuestionId,
                QuestionOrder = index + 1,
                SectionId = sections.Count > 0 ? sections[r.SectionOrder!.Value - 1].Id : null
            });
            _db.MockTestQuestions.AddRange(mockTestQuestions);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = test.Id }, new MockTestSummaryDto
            {
                Id = test.Id,
                BusinessId = test.BusinessId,
                Title = test.Title,
                ExamName = test.ExamName,
                ExamId = test.ExamId,
                TestType = test.TestType.ToString(),
                DurationMinutes = test.DurationMinutes,
                NegativeMarkingRatio = test.NegativeMarkingRatio,
                QuestionCount = refs.Count,
                Language = test.Language?.ToString(),
                ScheduledAt = test.ScheduledAt,
                EndAt = test.EndAt,
                Status = test.Status.ToString(),
                AvailabilityStatus = ComputeAvailability(test, DateTime.UtcNow),
                MaxAttempts = test.MaxAttempts,
                IsSectional = test.IsSectional,
                Sections = sections.Select(x => new MockTestSectionSummaryDto
                {
                    Order = x.SectionOrder,
                    Name = x.Name,
                    DurationMinutes = x.DurationMinutes,
                    QuestionCount = refs.Count(r => r.SectionOrder == x.SectionOrder)
                }).ToList(),
                ResultReleaseMode = test.ResultReleaseMode.ToString(),
                ResultDelayMinutes = test.ResultDelayMinutes,
                ResultReleaseAt = test.ResultReleaseAt
            });
        }

        // POST /api/mocktests/{id}/start -- SCORAM_TESTS' new attempt flow (start -> auto-save answer
        // -> submit, instead of the old one-shot POST /{id}/attempts). If the student already has an
        // InProgress attempt for this test, returns THAT one instead of creating a second (see the
        // unique index on (UserId, MockTestId, Status) -- this is what makes "Resume Test" work).
        [HttpPost("{id:guid}/start")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<TestAttemptStartResponseDto>> Start(Guid id)
        {
            var test = await _db.MockTests.Include(t => t.MockTestQuestions).Include(t => t.Sections).FirstOrDefaultAsync(t => t.Id == id);
            if (test == null) return NotFound(new { message = "Mock test not found." });
            if (test.Status != TestPublishStatus.Published) return NotFound(new { message = "Mock test not found." });

            var now = DateTime.UtcNow;
            if (test.ScheduledAt.HasValue && now < test.ScheduledAt.Value)
                return BadRequest(new { message = "This mock test hasn't started yet." });
            if (test.EndAt.HasValue && now > test.EndAt.Value)
                return BadRequest(new { message = "This mock test's window has closed." });

            var userId = User.GetUserId();

            var existing = await _db.StudentTestResults
                .Include(r => r.Answers)
                .FirstOrDefaultAsync(r => r.UserId == userId && r.MockTestId == id && r.Status == TestAttemptStatus.InProgress);
            if (existing != null)
            {
                existing.MockTest = test;
                // SECTION-WISE TIMING -- resume lands in the section whose clock is really running now.
                if (SectionedAttempts.Normalize(existing, now)) await _db.SaveChangesAsync();
                return Ok(TestAttemptsController.ToStartResponse(existing));
            }

            if (test.MaxAttempts.HasValue)
            {
                var usedAttempts = await _db.StudentTestResults.CountAsync(r => r.UserId == userId && r.MockTestId == id);
                // GAMIFICATION -- referral rewards can raise a student's effective attempt ceiling on
                // every attempt-capped mock test (see User.BonusMockAttempts / GamificationService.ApplyReferralAsync).
                var bonusAttempts = await _db.Users.Where(u => u.Id == userId).Select(u => u.BonusMockAttempts).FirstOrDefaultAsync();
                var effectiveMax = test.MaxAttempts.Value + bonusAttempts;
                if (usedAttempts >= effectiveMax)
                    return BadRequest(new { message = $"You've used all {effectiveMax} attempt(s) for this mock test." });
            }

            // Questions are laid out section by section (sections in their set order); inside a section they keep
            // the admin's order, or are shuffled when IsRandomOrder. A test without sections is one group.
            var sectionById = test.Sections.ToDictionary(sec => sec.Id);
            var orderedQuestions = test.MockTestQuestions
                .OrderBy(mq => mq.SectionId != null && sectionById.TryGetValue(mq.SectionId.Value, out var sec) ? sec.SectionOrder : 0)
                .ThenBy(mq => mq.QuestionOrder)
                .ToList();
            var refs = orderedQuestions.Select((mq, i) =>
            {
                MockTestSection? section = null;
                if (mq.SectionId != null) sectionById.TryGetValue(mq.SectionId.Value, out section);
                return new QuestionRef(mq.QuestionId, mq.QuestionBankQuestionId, i + 1, section?.SectionOrder, section?.Name);
            });
            var answers = await _attemptService.BuildSnapshotAnswersAsync(_db, refs);

            var laidOut = new List<StudentAnswer>();
            foreach (var group in answers.GroupBy(a => a.SectionOrder ?? 0).OrderBy(g => g.Key))
                laidOut.AddRange(test.IsRandomOrder ? group.OrderBy(_ => Random.Shared.Next()) : group.OrderBy(a => a.QuestionOrder));
            for (var i = 0; i < laidOut.Count; i++) laidOut[i].QuestionOrder = i + 1;
            answers = laidOut;

            var attempt = new StudentTestResult
            {
                TestKind = TestKind.Mock,
                MockTestId = test.Id,
                UserId = userId,
                NegativeMarkingRatio = test.NegativeMarkingRatio,
                Status = TestAttemptStatus.InProgress,
                StartedAt = now
            };
            // SECTION-WISE TIMING -- freeze the plan onto the attempt and start the clock of section 1.
            if (test.IsSectional)
            {
                var plan = test.Sections.OrderBy(sec => sec.SectionOrder)
                    .Where(sec => sec.DurationMinutes.HasValue)
                    .Select(sec => new SectionPlanItem(sec.SectionOrder, sec.Name, sec.DurationMinutes!.Value))
                    .ToList();
                if (plan.Count > 0)
                {
                    attempt.SectionPlanJson = SectionedAttempts.Serialize(plan);
                    attempt.CurrentSectionOrder = plan[0].Order;
                    attempt.CurrentSectionStartedAt = now;
                }
            }
            foreach (var a in answers) attempt.Answers.Add(a);

            _db.StudentTestResults.Add(attempt);
            await _db.SaveChangesAsync();

            attempt.MockTest = test;
            return Ok(TestAttemptsController.ToStartResponse(attempt));
        }

        // ---------- helpers ----------

        // SECTION-WISE TIMING / RESULT RELEASE fields shared by every MockTestSummaryDto this controller (and the
        // admin controller) builds. test.Sections and test.MockTestQuestions must be loaded.
        internal static void ApplySettingsSummary(MockTestSummaryDto dto, MockTest test)
        {
            dto.IsSectional = test.IsSectional;
            dto.Sections = MockTestRules.SummarizeSections(test)
                .Select(x => new MockTestSectionSummaryDto { Order = x.Order, Name = x.Name, DurationMinutes = x.DurationMinutes, QuestionCount = x.QuestionCount })
                .ToList();
            dto.ResultReleaseMode = test.ResultReleaseMode.ToString();
            dto.ResultDelayMinutes = test.ResultDelayMinutes;
            dto.ResultReleaseAt = test.ResultReleaseAt;
        }

        // Same tolerant parse as QuestionBankAdminController.ParseLanguage -- an empty/unrecognized
        // value just leaves the Mock Test's medium untagged rather than erroring the whole request.
        internal static PaperLanguage? ParseLanguage(string? raw) =>
            !string.IsNullOrWhiteSpace(raw) && Enum.TryParse<PaperLanguage>(raw, ignoreCase: true, out var parsed) ? parsed : null;

        internal static string ComputeAvailability(MockTest test, DateTime now)
        {
            if (test.Status == TestPublishStatus.Draft) return "Draft";
            if (test.Status == TestPublishStatus.Archived) return "Archived";
            if (test.ScheduledAt.HasValue && now < test.ScheduledAt.Value) return "Upcoming";
            if (test.EndAt.HasValue && now > test.EndAt.Value) return "Completed";
            return "Live";
        }

        private async Task<string?> ValidateQuestionRefsAsync(List<TestQuestionRefDto> refs)
        {
            var questionIds = refs.Where(r => r.QuestionId.HasValue).Select(r => r.QuestionId!.Value).Distinct().ToList();
            var qbQuestionIds = refs.Where(r => r.QuestionBankQuestionId.HasValue).Select(r => r.QuestionBankQuestionId!.Value).Distinct().ToList();

            if (questionIds.Count > 0)
            {
                var found = await _db.Questions.CountAsync(q => questionIds.Contains(q.Id));
                if (found != questionIds.Count) return "One or more question ids don't exist.";
            }
            if (qbQuestionIds.Count > 0)
            {
                var found = await _db.QuestionBankQuestions.CountAsync(q => qbQuestionIds.Contains(q.Id) && q.IsActive);
                if (found != qbQuestionIds.Count) return "One or more Question Bank question ids don't exist.";
            }
            return null;
        }
    }
}