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
    // Admin side of Quizzes Phase 2 (admin-curated Daily Quiz). Deliberately trimmed relative to
    // MockTestsAdminController -- no Duplicate/Reorder/GetAttempts/GetResultsSummary here yet; add
    // them later if an admin actually needs them, rather than speculatively matching MockTest's full
    // surface for a feature that's still finding its shape. Gated by the same ManageTests permission
    // as Mock/Practice Test admin, since this is the same kind of work.
    [ApiController]
    [Route("api/admin/quizzes")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class QuizzesAdminController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IAuditLogService _audit;

        public QuizzesAdminController(ScoramDbContext db, IAdminPermissionService permissions, IAuditLogService audit)
        {
            _db = db;
            _permissions = permissions;
            _audit = audit;
        }

        // GET /api/admin/quizzes?status=&page=&pageSize=
        [HttpGet]
        public async Task<ActionResult<PagedResult<QuizSummaryDto>>> List(
            [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.Quizzes.Include(q => q.QuizQuestions).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TestPublishStatus>(status, true, out var parsed))
                query = query.Where(q => q.Status == parsed);

            query = query.OrderByDescending(q => q.CreatedAt);
            var totalCount = await query.CountAsync();
            var quizzes = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var now = DateTime.UtcNow;
            var items = quizzes.Select(q => ToSummaryDto(q, now)).ToList();

            return Ok(new PagedResult<QuizSummaryDto> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
        }

        // GET /api/admin/quizzes/{id} -- includes the answer key (admin only).
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<QuizDetailDto>> GetById(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            var quiz = await _db.Quizzes
                .Include(q => q.QuizQuestions).ThenInclude(qq => qq.QuestionBankQuestion).ThenInclude(qb => qb!.Subject)
                .Include(q => q.QuizQuestions).ThenInclude(qq => qq.QuestionBankQuestion).ThenInclude(qb => qb!.Topic)
                .FirstOrDefaultAsync(q => q.Id == id);
            if (quiz == null) return NotFound();

            return Ok(new QuizDetailDto
            {
                Id = quiz.Id,
                Title = quiz.Title,
                Topic = quiz.Topic,
                DurationMinutes = quiz.DurationMinutes,
                NegativeMarkingRatio = quiz.NegativeMarkingRatio,
                AvailableFrom = quiz.AvailableFrom,
                AvailableTo = quiz.AvailableTo,
                MaxAttempts = quiz.MaxAttempts,
                Status = quiz.Status.ToString(),
                Questions = quiz.QuizQuestions.OrderBy(qq => qq.QuestionOrder).Select(qq => new QuizQuestionAdminDto
                {
                    QuizQuestionId = qq.Id,
                    QuestionOrder = qq.QuestionOrder,
                    QuestionBankQuestionId = qq.QuestionBankQuestionId,
                    QuestionText = qq.QuestionBankQuestion?.QuestionText ?? "",
                    Subject = qq.QuestionBankQuestion?.Subject?.Name ?? "",
                    Topic = qq.QuestionBankQuestion?.Topic?.Name ?? "",
                    CorrectOption = qq.QuestionBankQuestion?.CorrectOption.ToString() ?? ""
                }).ToList()
            });
        }

        // POST /api/admin/quizzes -- creates an empty Draft shell; add questions via the /questions
        // endpoint below, same two-step "create identity, then add questions" flow as Papers.
        [HttpPost]
        public async Task<ActionResult<QuizSummaryDto>> Create(QuizCreateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            var quiz = new Quiz
            {
                Title = dto.Title,
                Topic = dto.Topic,
                DurationMinutes = dto.DurationMinutes,
                NegativeMarkingRatio = dto.NegativeMarkingRatio,
                AvailableFrom = dto.AvailableFrom,
                AvailableTo = dto.AvailableTo,
                MaxAttempts = dto.MaxAttempts,
                Status = TestPublishStatus.Draft,
                CreatedByAdminId = User.GetAdminId(),
                CreatedAt = DateTime.UtcNow
            };
            _db.Quizzes.Add(quiz);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "Quiz.Create", "Quiz", quiz.Id, quiz.Title);

            return CreatedAtAction(nameof(GetById), new { id = quiz.Id }, ToSummaryDto(quiz, DateTime.UtcNow));
        }

        // PUT /api/admin/quizzes/{id} -- settings only, doesn't touch the question list.
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, QuizUpdateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            var quiz = await _db.Quizzes.FindAsync(id);
            if (quiz == null) return NotFound();

            quiz.Title = dto.Title;
            quiz.Topic = dto.Topic;
            quiz.DurationMinutes = dto.DurationMinutes;
            quiz.NegativeMarkingRatio = dto.NegativeMarkingRatio;
            quiz.AvailableFrom = dto.AvailableFrom;
            quiz.AvailableTo = dto.AvailableTo;
            quiz.MaxAttempts = dto.MaxAttempts;

            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "Quiz.Update", "Quiz", id);
            return NoContent();
        }

        // PATCH /api/admin/quizzes/{id}/status  { "status": "Draft" | "Published" | "Archived" }
        [HttpPatch("{id:guid}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateTestStatusDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();
            if (!Enum.TryParse<TestPublishStatus>(dto.Status, true, out var status))
                return BadRequest(new { message = "Status must be Draft, Published, or Archived." });

            var quiz = await _db.Quizzes.Include(q => q.QuizQuestions).FirstOrDefaultAsync(q => q.Id == id);
            if (quiz == null) return NotFound();

            if (status == TestPublishStatus.Published && quiz.QuizQuestions.Count == 0)
                return BadRequest(new { message = "Add at least one question before publishing." });

            quiz.Status = status;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), $"Quiz.{status}", "Quiz", id);
            return Ok(new { quiz.Id, Status = quiz.Status.ToString() });
        }

        // POST /api/admin/quizzes/{id}/questions -- bulk-add Question Bank questions, auto-numbered
        // sequentially after whatever's already on the quiz (see QuizQuestionsAddDto).
        [HttpPost("{id:guid}/questions")]
        public async Task<IActionResult> AddQuestions(Guid id, QuizQuestionsAddDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            var quiz = await _db.Quizzes.Include(q => q.QuizQuestions).FirstOrDefaultAsync(q => q.Id == id);
            if (quiz == null) return NotFound();

            var requestedIds = dto.QuestionBankQuestionIds.Distinct().ToList();
            var alreadyMapped = quiz.QuizQuestions.Select(qq => qq.QuestionBankQuestionId).ToHashSet();
            var toAdd = requestedIds.Where(qid => !alreadyMapped.Contains(qid)).ToList();
            if (toAdd.Count == 0)
                return Ok(new { added = 0, totalQuestions = quiz.QuizQuestions.Count });

            var validIds = await _db.QuestionBankQuestions
                .Where(q => toAdd.Contains(q.Id) && q.IsActive)
                .Select(q => q.Id)
                .ToListAsync();

            var nextOrder = quiz.QuizQuestions.Count > 0 ? quiz.QuizQuestions.Max(qq => qq.QuestionOrder) + 1 : 1;
            var newLinks = validIds.Select((qid, i) => new QuizQuestion
            {
                QuizId = id,
                QuestionBankQuestionId = qid,
                QuestionOrder = nextOrder + i
            }).ToList();

            _db.QuizQuestions.AddRange(newLinks);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "Quiz.AddQuestions", "Quiz", id, $"{newLinks.Count} question(s)");

            return Ok(new { added = newLinks.Count, totalQuestions = quiz.QuizQuestions.Count + newLinks.Count });
        }

        // DELETE /api/admin/quizzes/{id}/questions/{quizQuestionId}
        [HttpDelete("{id:guid}/questions/{quizQuestionId:guid}")]
        public async Task<IActionResult> RemoveQuestion(Guid id, Guid quizQuestionId)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            var qq = await _db.QuizQuestions.FirstOrDefaultAsync(q => q.Id == quizQuestionId && q.QuizId == id);
            if (qq == null) return NotFound();

            // Existing attempts already snapshotted this question's content into their own
            // StudentAnswer rows -- removing it here doesn't touch those, past results stay accurate.
            _db.QuizQuestions.Remove(qq);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "Quiz.RemoveQuestion", "Quiz", id);
            return NoContent();
        }

        internal static QuizSummaryDto ToSummaryDto(Quiz q, DateTime now) => new QuizSummaryDto
        {
            Id = q.Id,
            Title = q.Title,
            Topic = q.Topic,
            DurationMinutes = q.DurationMinutes,
            NegativeMarkingRatio = q.NegativeMarkingRatio,
            QuestionCount = q.QuizQuestions.Count,
            AvailableFrom = q.AvailableFrom,
            AvailableTo = q.AvailableTo,
            Status = q.Status.ToString(),
            AvailabilityStatus = ComputeAvailability(q, now),
            MaxAttempts = q.MaxAttempts
        };

        internal static string ComputeAvailability(Quiz q, DateTime now)
        {
            if (q.Status == TestPublishStatus.Draft) return "Draft";
            if (q.Status == TestPublishStatus.Archived) return "Archived";
            if (q.AvailableFrom.HasValue && now < q.AvailableFrom.Value) return "Upcoming";
            if (q.AvailableTo.HasValue && now > q.AvailableTo.Value) return "Completed";
            return "Live";
        }
    }
}
