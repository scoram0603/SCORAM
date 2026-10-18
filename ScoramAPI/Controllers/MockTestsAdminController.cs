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
    // SCORAM_TESTS -- admin side of Mock Tests. MockTestsController (student-facing) only shows
    // Published tests; everything here works across all statuses since an admin needs to see/edit
    // Draft and Archived tests too.
    [ApiController]
    [Route("api/admin/mocktests")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class MockTestsAdminController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IAuditLogService _audit;
        private readonly INotificationFanOutService _fanOut;

        public MockTestsAdminController(ScoramDbContext db, IAdminPermissionService permissions, IAuditLogService audit,
            INotificationFanOutService fanOut)
        {
            _db = db;
            _permissions = permissions;
            _audit = audit;
            _fanOut = fanOut;
        }

        // GET /api/admin/mocktests?status=&search=&page=&pageSize=
        // search matches the Business ID (MCK0001), Title or Exam name, case-insensitive.
        [HttpGet]
        public async Task<ActionResult<PagedResult<MockTestSummaryDto>>> List(
            [FromQuery] string? status, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.MockTests.Include(t => t.MockTestQuestions).Include(t => t.Sections).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TestPublishStatus>(status, true, out var parsed))
                query = query.Where(t => t.Status == parsed);

            var term = search?.Trim();
            if (!string.IsNullOrEmpty(term))
                query = query.Where(t => t.Title.Contains(term) || t.ExamName.Contains(term)
                    || (t.BusinessId != null && t.BusinessId.Contains(term)));

            query = query.OrderByDescending(t => t.CreatedAt);
            var totalCount = await query.CountAsync();
            var tests = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var now = DateTime.UtcNow;
            var testIds = tests.Select(t => t.Id).ToList();
            var attemptCounts = testIds.Count == 0
                ? new Dictionary<Guid, int>()
                : await _db.StudentTestResults
                    .Where(r => r.MockTestId != null && testIds.Contains(r.MockTestId.Value)
                        && (r.Status == TestAttemptStatus.Submitted || r.Status == TestAttemptStatus.AutoSubmitted))
                    .Select(r => new { MockTestId = r.MockTestId!.Value, r.UserId })
                    .Distinct()
                    .GroupBy(r => r.MockTestId)
                    .Select(g => new { MockTestId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.MockTestId, g => g.Count);

            var items = tests.Select(t => new MockTestSummaryDto
            {
                Id = t.Id,
                BusinessId = t.BusinessId,
                Title = t.Title,
                ExamName = t.ExamName,
                TestType = t.TestType.ToString(),
                DurationMinutes = t.DurationMinutes,
                NegativeMarkingRatio = t.NegativeMarkingRatio,
                QuestionCount = t.MockTestQuestions.Count,
                Language = t.Language?.ToString(),
                ScheduledAt = t.ScheduledAt,
                EndAt = t.EndAt,
                Status = t.Status.ToString(),
                AvailabilityStatus = MockTestsController.ComputeAvailability(t, now),
                MaxAttempts = t.MaxAttempts,
                AttemptCount = attemptCounts.GetValueOrDefault(t.Id, 0)
            }).ToList();
            for (var i = 0; i < items.Count; i++) MockTestsController.ApplySettingsSummary(items[i], tests[i]);

            return Ok(new PagedResult<MockTestSummaryDto> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
        }

        // GET /api/admin/mocktests/{id} -- includes the answer key (admin only).
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<object>> GetById(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            var test = await _db.MockTests
                .Include(t => t.MockTestQuestions).ThenInclude(mq => mq.Question)
                .Include(t => t.MockTestQuestions).ThenInclude(mq => mq.QuestionBankQuestion)
                .Include(t => t.Sections)
                .FirstOrDefaultAsync(t => t.Id == id);
            if (test == null) return NotFound();

            var sectionOrderById = test.Sections.ToDictionary(x => x.Id, x => x.SectionOrder);

            return Ok(new
            {
                test.Id,
                test.BusinessId,
                test.Title,
                test.ExamName,
                TestType = test.TestType.ToString(),
                test.DurationMinutes,
                test.NegativeMarkingRatio,
                test.IsRandomOrder,
                test.IsShuffleOptions,
                Language = test.Language?.ToString(),
                test.ScheduledAt,
                test.EndAt,
                Status = test.Status.ToString(),
                test.MaxAttempts,
                test.Instructions,
                test.IsSectional,
                Sections = test.Sections.OrderBy(x => x.SectionOrder).Select(x => new
                {
                    Order = x.SectionOrder,
                    x.Name,
                    x.DurationMinutes,
                    QuestionCount = test.MockTestQuestions.Count(mq => mq.SectionId == x.Id)
                }),
                ResultReleaseMode = test.ResultReleaseMode.ToString(),
                test.ResultDelayMinutes,
                test.ResultReleaseAt,
                Questions = test.MockTestQuestions.OrderBy(mq => mq.QuestionOrder).Select(mq => new
                {
                    mq.Id,
                    mq.QuestionOrder,
                    SectionOrder = mq.SectionId != null && sectionOrderById.TryGetValue(mq.SectionId.Value, out var so) ? (int?)so : null,
                    QuestionId = mq.QuestionId,
                    QuestionBankQuestionId = mq.QuestionBankQuestionId,
                    IsQuestionBank = mq.QuestionBankQuestionId != null,
                    QuestionText = mq.Question?.QuestionText ?? mq.QuestionBankQuestion?.QuestionText,
                    OptionA = mq.Question?.OptionA ?? mq.QuestionBankQuestion?.OptionA,
                    OptionB = mq.Question?.OptionB ?? mq.QuestionBankQuestion?.OptionB,
                    OptionC = mq.Question?.OptionC ?? mq.QuestionBankQuestion?.OptionC,
                    OptionD = mq.Question?.OptionD ?? mq.QuestionBankQuestion?.OptionD,
                    CorrectOption = (mq.Question?.CorrectOption ?? mq.QuestionBankQuestion?.CorrectOption)?.ToString()
                })
            });
        }

        // PUT /api/admin/mocktests/{id} -- settings only, doesn't touch the question list (use the
        // /questions endpoints below for that).
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, MockTestUpdateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            var test = await _db.MockTests.Include(t => t.Sections).FirstOrDefaultAsync(t => t.Id == id);
            if (test == null) return NotFound();

            // RESULT RELEASE + SECTIONS. Sections can be renamed / re-timed here, but not added or removed
            // (the questions are already assigned to them); omit Sections to leave them untouched.
            if (!MockTestRules.TryParseReleaseMode(dto.ResultReleaseMode, out var releaseMode))
                return BadRequest(new { message = "Result release must be Instant, AfterDelay, OnDate or AfterWindowEnds." });
            var releaseError = MockTestRules.ValidateResultRelease(releaseMode, dto.ResultDelayMinutes, dto.ResultReleaseAt, dto.EndAt);
            if (releaseError != null) return BadRequest(new { message = releaseError });

            var orderedSections = test.Sections.OrderBy(x => x.SectionOrder).ToList();
            var sectionsChanged = false;
            var isSectional = test.IsSectional;
            int sectionTotal = 0;
            if (dto.Sections != null && (dto.Sections.Count > 0 || orderedSections.Count > 0))
            {
                if (dto.Sections.Count != orderedSections.Count)
                    return BadRequest(new { message = "The number of sections can't be changed after creation. Create a new test instead." });
                var input = dto.Sections.Select(x => (x.Name ?? string.Empty, x.DurationMinutes)).ToList();
                var sectionError = MockTestRules.ValidateSections(input, out isSectional, out sectionTotal);
                if (sectionError != null) return BadRequest(new { message = sectionError });
                for (var i = 0; i < orderedSections.Count; i++)
                {
                    orderedSections[i].Name = input[i].Item1.Trim();
                    orderedSections[i].DurationMinutes = input[i].DurationMinutes;
                }
                sectionsChanged = true;
            }

            // "MY EXAMS" -- same ExamId->ExamName sync as MockTestsController.Create; a legacy
            // caller that only sends ExamName (no ExamId) keeps updating exactly as before.
            var examName = dto.ExamName;
            if (dto.ExamId.HasValue)
            {
                var exam = await _db.Exams.FindAsync(dto.ExamId.Value);
                if (exam == null) return BadRequest(new { message = "Selected exam could not be found." });
                examName = exam.Name;
            }

            test.Title = dto.Title;
            test.ExamName = examName;
            test.ExamId = dto.ExamId;
            test.TestType = dto.TestType;
            test.DurationMinutes = sectionsChanged && isSectional ? sectionTotal : dto.DurationMinutes;
            if (sectionsChanged) test.IsSectional = isSectional;
            test.NegativeMarkingRatio = dto.NegativeMarkingRatio;
            test.IsRandomOrder = dto.IsRandomOrder;
            test.IsShuffleOptions = dto.IsShuffleOptions;
            test.Language = MockTestsController.ParseLanguage(dto.Language);
            test.ScheduledAt = dto.ScheduledAt;
            test.EndAt = dto.EndAt;
            test.MaxAttempts = dto.MaxAttempts;
            test.Instructions = dto.Instructions;
            test.ResultReleaseMode = releaseMode;
            test.ResultDelayMinutes = releaseMode == ResultReleaseMode.AfterDelay ? dto.ResultDelayMinutes : null;
            test.ResultReleaseAt = releaseMode == ResultReleaseMode.OnDate ? dto.ResultReleaseAt : null;

            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "MockTest.Update", "MockTest", id);
            return NoContent();
        }

        // PATCH /api/admin/mocktests/{id}/status  { "status": "Draft" | "Published" | "Archived" }
        [HttpPatch("{id:guid}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateTestStatusDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();
            if (!Enum.TryParse<TestPublishStatus>(dto.Status, true, out var status))
                return BadRequest(new { message = "Status must be Draft, Published, or Archived." });

            var test = await _db.MockTests.Include(t => t.MockTestQuestions).FirstOrDefaultAsync(t => t.Id == id);
            if (test == null) return NotFound();

            if (status == TestPublishStatus.Published && test.MockTestQuestions.Count == 0)
                return BadRequest(new { message = "Add at least one question before publishing." });

            var wasPublished = test.Status == TestPublishStatus.Published;
            test.Status = status;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), $"MockTest.{status}", "MockTest", id);

            // PUSH: tell students preparing for THIS exam (My Exams) -- and nobody else. Only on a real
            // Draft/Archived -> Published transition (re-saving Published stays silent), and only for a
            // Mock Test that has an ExamId: legacy rows tagged only by free-text ExamName can't be
            // matched to My Exams reliably, so they are skipped rather than broadcast. DedupKey makes a
            // double click harmless.
            if (status == TestPublishStatus.Published && !wasPublished && test.ExamId.HasValue)
            {
                _fanOut.Queue(new FanOutRequest
                {
                    Audience = FanOutAudience.ExamAudience,
                    ExamIds = new[] { test.ExamId.Value },
                    Type = NotificationType.NewMockTest,
                    Title = "New mock test available",
                    Body = $"{test.Title} is now available.",
                    LinkUrl = "/mock-tests",
                    EntityType = "MockTest",
                    EntityId = test.Id.ToString(),
                    DedupKey = $"NewMockTest:{test.Id}"
                });
            }
            return Ok(new { test.Id, Status = test.Status.ToString() });
        }

        // POST /api/admin/mocktests/{id}/duplicate -- copies settings + questions as a new Draft, so
        // an admin can reuse a past paper as a starting point without affecting its (possibly already
        // attempted) original.
        [HttpPost("{id:guid}/duplicate")]
        public async Task<ActionResult<MockTestSummaryDto>> Duplicate(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            var source = await _db.MockTests.Include(t => t.MockTestQuestions).Include(t => t.Sections).FirstOrDefaultAsync(t => t.Id == id);
            if (source == null) return NotFound();

            var adminId = User.GetAdminId();
            var copy = new MockTest
            {
                Title = $"{source.Title} (Copy)",
                ExamName = source.ExamName,
                ExamId = source.ExamId,
                IsSectional = source.IsSectional,
                ResultReleaseMode = source.ResultReleaseMode,
                ResultDelayMinutes = source.ResultDelayMinutes,
                // A fixed release date is usually in the past by the time a test is reused -> don't carry it over.
                ResultReleaseAt = null,
                TestType = source.TestType,
                DurationMinutes = source.DurationMinutes,
                NegativeMarkingRatio = source.NegativeMarkingRatio,
                IsRandomOrder = source.IsRandomOrder,
                IsShuffleOptions = source.IsShuffleOptions,
                Language = source.Language,
                Instructions = source.Instructions,
                MaxAttempts = source.MaxAttempts,
                Status = TestPublishStatus.Draft,
                CreatedByAdminId = adminId,
                CreatedAt = DateTime.UtcNow
            };
            _db.MockTests.Add(copy);
            await _db.SaveChangesAsync();

            if (copy.ResultReleaseMode == ResultReleaseMode.OnDate) copy.ResultReleaseMode = ResultReleaseMode.Instant;

            var sectionMap = new Dictionary<Guid, Guid>();
            foreach (var sec in source.Sections.OrderBy(x => x.SectionOrder))
            {
                var newSec = new MockTestSection { MockTestId = copy.Id, Name = sec.Name, SectionOrder = sec.SectionOrder, DurationMinutes = sec.DurationMinutes };
                _db.MockTestSections.Add(newSec);
                sectionMap[sec.Id] = newSec.Id; // Guid is client-generated on Add
            }

            var copiedQuestions = source.MockTestQuestions.OrderBy(q => q.QuestionOrder).Select(q => new MockTestQuestion
            {
                MockTestId = copy.Id,
                QuestionId = q.QuestionId,
                QuestionBankQuestionId = q.QuestionBankQuestionId,
                QuestionOrder = q.QuestionOrder,
                SectionId = q.SectionId != null && sectionMap.TryGetValue(q.SectionId.Value, out var ns) ? ns : null
            });
            _db.MockTestQuestions.AddRange(copiedQuestions);
            await _db.SaveChangesAsync();

            await _audit.LogAsync(adminId, "MockTest.Duplicate", "MockTest", copy.Id, $"from {source.Id}");
            return Ok(new MockTestSummaryDto { Id = copy.Id, Title = copy.Title, ExamName = copy.ExamName, TestType = copy.TestType.ToString(), Status = copy.Status.ToString() });
        }

        // POST /api/admin/mocktests/{id}/questions -- append one or more questions (Method 1: select
        // from Question Bank/legacy Questions, or Method 3: a single ref at a time -- both are the
        // same call with a 1-item list).
        [HttpPost("{id:guid}/questions")]
        public async Task<IActionResult> AddQuestions(Guid id, List<TestQuestionRefDto> refs)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();
            if (refs == null || refs.Count == 0) return BadRequest(new { message = "Provide at least one question." });

            var test = await _db.MockTests.Include(t => t.MockTestQuestions).Include(t => t.Sections).FirstOrDefaultAsync(t => t.Id == id);
            if (test == null) return NotFound();

            var sectionByOrder = test.Sections.ToDictionary(x => x.SectionOrder);
            if (test.IsSectional && refs.Any(r => r.SectionOrder == null || !sectionByOrder.ContainsKey(r.SectionOrder.Value)))
                return BadRequest(new { message = "This test has sections -- every added question needs a valid sectionOrder." });

            var nextOrder = test.MockTestQuestions.Count > 0 ? test.MockTestQuestions.Max(q => q.QuestionOrder) + 1 : 1;
            var toAdd = refs.Select((r, i) => new MockTestQuestion
            {
                MockTestId = id,
                QuestionId = r.QuestionId,
                QuestionBankQuestionId = r.QuestionBankQuestionId,
                QuestionOrder = nextOrder + i,
                SectionId = test.IsSectional ? sectionByOrder[r.SectionOrder!.Value].Id : null
            }).ToList();

            _db.MockTestQuestions.AddRange(toAdd);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "MockTest.AddQuestions", "MockTest", id, $"{toAdd.Count} question(s)");

            return Ok(new { added = toAdd.Count, totalQuestions = test.MockTestQuestions.Count + toAdd.Count });
        }

        // DELETE /api/admin/mocktests/{id}/questions/{mockTestQuestionId}
        [HttpDelete("{id:guid}/questions/{mockTestQuestionId:guid}")]
        public async Task<IActionResult> RemoveQuestion(Guid id, Guid mockTestQuestionId)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            var mtq = await _db.MockTestQuestions.FirstOrDefaultAsync(q => q.Id == mockTestQuestionId && q.MockTestId == id);
            if (mtq == null) return NotFound();

            // Existing attempts already snapshotted this question's content into their own
            // StudentAnswer rows (SCORAM_TESTS) -- removing it from the paper here doesn't touch
            // those, so past results stay accurate even though the question is no longer on the paper.
            _db.MockTestQuestions.Remove(mtq);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "MockTest.RemoveQuestion", "MockTest", id);
            return NoContent();
        }

        // PUT /api/admin/mocktests/{id}/questions/reorder  -- body: ordered list of MockTestQuestion ids
        [HttpPut("{id:guid}/questions/reorder")]
        public async Task<IActionResult> ReorderQuestions(Guid id, [FromBody] List<Guid> orderedQuestionIds)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            var questions = await _db.MockTestQuestions.Where(q => q.MockTestId == id).ToListAsync();
            var byId = questions.ToDictionary(q => q.Id);

            for (var i = 0; i < orderedQuestionIds.Count; i++)
            {
                if (byId.TryGetValue(orderedQuestionIds[i], out var q)) q.QuestionOrder = i + 1;
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // GET /api/admin/mocktests/{id}/attempts?page=&pageSize= -- every student's attempt on this
        // paper, most recent first.
        [HttpGet("{id:guid}/attempts")]
        public async Task<ActionResult<PagedResult<object>>> GetAttempts(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.StudentTestResults.Include(r => r.User).Where(r => r.MockTestId == id).OrderByDescending(r => r.StartedAt);
            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(r => new
                {
                    r.Id,
                    StudentName = r.User != null ? r.User.FullName : "Unknown",
                    Status = r.Status.ToString(),
                    r.Score,
                    r.CorrectCount,
                    r.WrongCount,
                    r.SkippedCount,
                    r.TimeTakenSeconds,
                    r.StartedAt,
                    r.AttemptedAt
                }).ToListAsync();

            return Ok(new PagedResult<object> { Items = items.Cast<object>().ToList(), TotalCount = totalCount, Page = page, PageSize = pageSize });
        }

        // GET /api/admin/mocktests/{id}/results -- aggregate + question-wise performance across every
        // SUBMITTED attempt (spec: "View question-wise performance").
        [HttpGet("{id:guid}/results")]
        public async Task<ActionResult<object>> GetResultsSummary(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            var submitted = _db.StudentTestResults.Where(r => r.MockTestId == id && r.Status != TestAttemptStatus.InProgress);
            var attemptCount = await submitted.CountAsync();
            var avgScore = attemptCount > 0 ? await submitted.AverageAsync(r => (double)r.Score) : 0;

            var questionStats = await _db.StudentAnswers
                .Where(a => a.StudentTestResult!.MockTestId == id && a.StudentTestResult!.Status != TestAttemptStatus.InProgress)
                .GroupBy(a => a.QuestionOrder)
                .Select(g => new
                {
                    QuestionOrder = g.Key,
                    QuestionText = g.First().QuestionTextSnapshot,
                    TotalAttempted = g.Count(a => a.SelectedOption != null),
                    CorrectCount = g.Count(a => a.IsCorrect),
                    SkippedCount = g.Count(a => a.SelectedOption == null)
                })
                .OrderBy(q => q.QuestionOrder)
                .ToListAsync();

            return Ok(new { attemptCount, averageScore = Math.Round(avgScore, 2), questions = questionStats });
        }
    }
}
