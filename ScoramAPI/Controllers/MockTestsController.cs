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

        public MockTestsController(ScoramDbContext db, ITestAttemptService attemptService)
        {
            _db = db;
            _attemptService = attemptService;
        }

        // GET /api/mocktests?examName=&testType=&page=&pageSize=
        // SCORAM_TESTS: now filters to Status == Published only (spec: "Prevent... Accessing
        // unpublished Mock Tests"). IMPORTANT migration note: every MockTest row that existed BEFORE
        // this field was added must be backfilled to Published, or they'll vanish from this list --
        // see the migration instructions.
        [HttpGet]
        public async Task<ActionResult<PagedResult<MockTestSummaryDto>>> List(
            string? examName, string? testType, int page = 1, int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.MockTests.Where(t => t.Status == TestPublishStatus.Published).AsQueryable();
            if (!string.IsNullOrWhiteSpace(examName)) query = query.Where(t => t.ExamName == examName);
            if (!string.IsNullOrWhiteSpace(testType)) query = query.Where(t => t.TestType.ToString() == testType);

            var totalCount = await query.CountAsync();

            var tests = await query
                .OrderByDescending(t => t.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Include(t => t.MockTestQuestions)
                .ToListAsync();

            var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
            Dictionary<Guid, int> myAttemptCounts = new();
            if (isAuthenticated && tests.Count > 0)
            {
                var userId = User.GetUserId();
                var testIds = tests.Select(t => t.Id).ToList();
                myAttemptCounts = await _db.StudentTestResults
                    .Where(r => r.UserId == userId && r.MockTestId != null && testIds.Contains(r.MockTestId.Value))
                    .GroupBy(r => r.MockTestId!.Value)
                    .Select(g => new { MockTestId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.MockTestId, g => g.Count);
            }

            var now = DateTime.UtcNow;
            var items = tests.Select(t => new MockTestSummaryDto
            {
                Id = t.Id,
                Title = t.Title,
                ExamName = t.ExamName,
                TestType = t.TestType.ToString(),
                DurationMinutes = t.DurationMinutes,
                NegativeMarkingRatio = t.NegativeMarkingRatio,
                QuestionCount = t.MockTestQuestions.Count,
                Instructions = t.Instructions,
                ScheduledAt = t.ScheduledAt,
                EndAt = t.EndAt,
                Status = t.Status.ToString(),
                AvailabilityStatus = ComputeAvailability(t, now),
                MaxAttempts = t.MaxAttempts,
                MyAttemptCount = isAuthenticated ? myAttemptCounts.GetValueOrDefault(t.Id, 0) : null
            }).ToList();

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
            var test = await _db.MockTests.Include(t => t.MockTestQuestions).FirstOrDefaultAsync(t => t.Id == id);
            if (test == null) return NotFound();

            var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
            var myAttemptCount = 0;
            if (isAuthenticated)
            {
                var userId = User.GetUserId();
                myAttemptCount = await _db.StudentTestResults.CountAsync(r => r.UserId == userId && r.MockTestId == id);
            }

            return Ok(new MockTestSummaryDto
            {
                Id = test.Id,
                Title = test.Title,
                ExamName = test.ExamName,
                TestType = test.TestType.ToString(),
                DurationMinutes = test.DurationMinutes,
                NegativeMarkingRatio = test.NegativeMarkingRatio,
                QuestionCount = test.MockTestQuestions.Count,
                Instructions = test.Instructions,
                ScheduledAt = test.ScheduledAt,
                EndAt = test.EndAt,
                Status = test.Status.ToString(),
                AvailabilityStatus = ComputeAvailability(test, DateTime.UtcNow),
                MaxAttempts = test.MaxAttempts,
                MyAttemptCount = isAuthenticated ? myAttemptCount : null
            });
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

            var items = rows.Select(r =>
            {
                var maxPossible = r.CorrectCount + r.WrongCount + r.SkippedCount;
                return new AttemptSummaryDto
                {
                    AttemptId = r.Id,
                    MockTestTitle = r.MockTest!.Title,
                    ExamName = r.MockTest.ExamName,
                    Score = r.Score,
                    MaxPossibleScore = maxPossible,
                    AccuracyPercent = maxPossible == 0 ? 0 : Math.Round((decimal)r.CorrectCount / maxPossible * 100, 2),
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

            if (!Enum.TryParse<TestPublishStatus>(dto.Status, true, out var status))
                status = TestPublishStatus.Draft;

            var test = new MockTest
            {
                Title = dto.Title,
                ExamName = dto.ExamName,
                TestType = dto.TestType,
                DurationMinutes = dto.DurationMinutes,
                NegativeMarkingRatio = dto.NegativeMarkingRatio,
                IsRandomOrder = dto.IsRandomOrder,
                IsShuffleOptions = dto.IsShuffleOptions,
                ScheduledAt = dto.ScheduledAt,
                EndAt = dto.EndAt,
                Status = status,
                MaxAttempts = dto.MaxAttempts,
                Instructions = dto.Instructions,
                CreatedByAdminId = adminId,
                CreatedAt = DateTime.UtcNow
            };

            _db.MockTests.Add(test);
            await _db.SaveChangesAsync();

            var mockTestQuestions = refs.Select((r, index) => new MockTestQuestion
            {
                MockTestId = test.Id,
                QuestionId = r.QuestionId,
                QuestionBankQuestionId = r.QuestionBankQuestionId,
                QuestionOrder = index + 1
            });
            _db.MockTestQuestions.AddRange(mockTestQuestions);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = test.Id }, new MockTestSummaryDto
            {
                Id = test.Id,
                Title = test.Title,
                ExamName = test.ExamName,
                TestType = test.TestType.ToString(),
                DurationMinutes = test.DurationMinutes,
                NegativeMarkingRatio = test.NegativeMarkingRatio,
                QuestionCount = refs.Count,
                ScheduledAt = test.ScheduledAt,
                EndAt = test.EndAt,
                Status = test.Status.ToString(),
                AvailabilityStatus = ComputeAvailability(test, DateTime.UtcNow),
                MaxAttempts = test.MaxAttempts
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
            var test = await _db.MockTests.Include(t => t.MockTestQuestions).FirstOrDefaultAsync(t => t.Id == id);
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

            var orderedQuestions = test.MockTestQuestions.OrderBy(mq => mq.QuestionOrder).ToList();
            var refs = orderedQuestions.Select((mq, i) => new QuestionRef(mq.QuestionId, mq.QuestionBankQuestionId, i + 1));
            var answers = await _attemptService.BuildSnapshotAnswersAsync(_db, refs);

            if (test.IsRandomOrder)
            {
                var shuffled = answers.OrderBy(_ => Random.Shared.Next()).ToList();
                for (var i = 0; i < shuffled.Count; i++) shuffled[i].QuestionOrder = i + 1;
                answers = shuffled;
            }

            var attempt = new StudentTestResult
            {
                TestKind = TestKind.Mock,
                MockTestId = test.Id,
                UserId = userId,
                NegativeMarkingRatio = test.NegativeMarkingRatio,
                Status = TestAttemptStatus.InProgress,
                StartedAt = now
            };
            foreach (var a in answers) attempt.Answers.Add(a);

            _db.StudentTestResults.Add(attempt);
            await _db.SaveChangesAsync();

            attempt.MockTest = test;
            return Ok(TestAttemptsController.ToStartResponse(attempt));
        }

        // ---------- helpers ----------

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