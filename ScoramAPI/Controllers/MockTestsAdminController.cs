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

        public MockTestsAdminController(ScoramDbContext db, IAdminPermissionService permissions, IAuditLogService audit)
        {
            _db = db;
            _permissions = permissions;
            _audit = audit;
        }

        // GET /api/admin/mocktests?status=&page=&pageSize=
        [HttpGet]
        public async Task<ActionResult<PagedResult<MockTestSummaryDto>>> List(
            [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.MockTests.Include(t => t.MockTestQuestions).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TestPublishStatus>(status, true, out var parsed))
                query = query.Where(t => t.Status == parsed);

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
                .FirstOrDefaultAsync(t => t.Id == id);
            if (test == null) return NotFound();

            return Ok(new
            {
                test.Id,
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
                Questions = test.MockTestQuestions.OrderBy(mq => mq.QuestionOrder).Select(mq => new
                {
                    mq.Id,
                    mq.QuestionOrder,
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

            var test = await _db.MockTests.FindAsync(id);
            if (test == null) return NotFound();

            test.Title = dto.Title;
            test.ExamName = dto.ExamName;
            test.TestType = dto.TestType;
            test.DurationMinutes = dto.DurationMinutes;
            test.NegativeMarkingRatio = dto.NegativeMarkingRatio;
            test.IsRandomOrder = dto.IsRandomOrder;
            test.IsShuffleOptions = dto.IsShuffleOptions;
            test.Language = MockTestsController.ParseLanguage(dto.Language);
            test.ScheduledAt = dto.ScheduledAt;
            test.EndAt = dto.EndAt;
            test.MaxAttempts = dto.MaxAttempts;
            test.Instructions = dto.Instructions;

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

            test.Status = status;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), $"MockTest.{status}", "MockTest", id);
            return Ok(new { test.Id, Status = test.Status.ToString() });
        }

        // POST /api/admin/mocktests/{id}/duplicate -- copies settings + questions as a new Draft, so
        // an admin can reuse a past paper as a starting point without affecting its (possibly already
        // attempted) original.
        [HttpPost("{id:guid}/duplicate")]
        public async Task<ActionResult<MockTestSummaryDto>> Duplicate(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            var source = await _db.MockTests.Include(t => t.MockTestQuestions).FirstOrDefaultAsync(t => t.Id == id);
            if (source == null) return NotFound();

            var adminId = User.GetAdminId();
            var copy = new MockTest
            {
                Title = $"{source.Title} (Copy)",
                ExamName = source.ExamName,
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

            var copiedQuestions = source.MockTestQuestions.OrderBy(q => q.QuestionOrder).Select(q => new MockTestQuestion
            {
                MockTestId = copy.Id,
                QuestionId = q.QuestionId,
                QuestionBankQuestionId = q.QuestionBankQuestionId,
                QuestionOrder = q.QuestionOrder
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

            var test = await _db.MockTests.Include(t => t.MockTestQuestions).FirstOrDefaultAsync(t => t.Id == id);
            if (test == null) return NotFound();

            var nextOrder = test.MockTestQuestions.Count > 0 ? test.MockTestQuestions.Max(q => q.QuestionOrder) + 1 : 1;
            var toAdd = refs.Select((r, i) => new MockTestQuestion
            {
                MockTestId = id,
                QuestionId = r.QuestionId,
                QuestionBankQuestionId = r.QuestionBankQuestionId,
                QuestionOrder = nextOrder + i
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
