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
    // SCORAM_TESTS -- admin side of Practice Test templates (spec section B: admin-curated,
    // browsable Practice Tests). Ad-hoc/student-generated Practice attempts (section A) don't involve
    // a template at all and have nothing to manage here.
    [ApiController]
    [Route("api/admin/practice-tests")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class PracticeTestsAdminController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IAuditLogService _audit;

        public PracticeTestsAdminController(ScoramDbContext db, IAdminPermissionService permissions, IAuditLogService audit)
        {
            _db = db;
            _permissions = permissions;
            _audit = audit;
        }

        // GET /api/admin/practice-tests?status=&page=&pageSize=
        [HttpGet]
        public async Task<ActionResult<PagedResult<PracticeTestTemplateAdminDto>>> List(
            [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.PracticeTestTemplates
                .Include(t => t.Subject).Include(t => t.Topic).Include(t => t.Exam)
                .Include(t => t.Questions).Include(t => t.CreatedByAdmin)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TestPublishStatus>(status, true, out var parsed))
                query = query.Where(t => t.Status == parsed);

            query = query.OrderByDescending(t => t.CreatedAt);
            var totalCount = await query.CountAsync();
            var templates = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var templateIds = templates.Select(t => t.Id).ToList();
            var attemptCounts = await _db.StudentTestResults
                .Where(r => r.PracticeTestTemplateId != null && templateIds.Contains(r.PracticeTestTemplateId.Value))
                .GroupBy(r => r.PracticeTestTemplateId!.Value)
                .Select(g => new { TemplateId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.TemplateId, g => g.Count);

            var items = templates.Select(t => ToAdminDto(t, attemptCounts.GetValueOrDefault(t.Id, 0))).ToList();
            return Ok(new PagedResult<PracticeTestTemplateAdminDto> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
        }

        // GET /api/admin/practice-tests/{id}
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<PracticeTestTemplateAdminDto>> GetById(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            var template = await _db.PracticeTestTemplates
                .Include(t => t.Subject).Include(t => t.Topic).Include(t => t.Exam)
                .Include(t => t.Questions).Include(t => t.CreatedByAdmin)
                .FirstOrDefaultAsync(t => t.Id == id);
            if (template == null) return NotFound();

            var attemptCount = await _db.StudentTestResults.CountAsync(r => r.PracticeTestTemplateId == id);
            return Ok(ToAdminDto(template, attemptCount));
        }

        // POST /api/admin/practice-tests
        [HttpPost]
        public async Task<ActionResult<PracticeTestTemplateAdminDto>> Create(PracticeTestTemplateCreateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            var validationError = await ValidateAsync(dto);
            if (validationError != null) return BadRequest(new { message = validationError });

            if (!Enum.TryParse<TestPublishStatus>(dto.Status, true, out var status)) status = TestPublishStatus.Draft;
            DifficultyLevel? difficulty = null;
            if (!string.IsNullOrWhiteSpace(dto.Difficulty) && Enum.TryParse<DifficultyLevel>(dto.Difficulty, true, out var parsedDifficulty))
                difficulty = parsedDifficulty;

            var adminId = User.GetAdminId();
            var template = new PracticeTestTemplate
            {
                Title = dto.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                SubjectId = dto.SubjectId,
                TopicId = dto.TopicId,
                ExamId = dto.ExamId,
                YearFrom = dto.YearFrom,
                YearTo = dto.YearTo,
                Difficulty = difficulty,
                QuestionCount = dto.QuestionCount,
                DurationMinutes = dto.DurationMinutes,
                NegativeMarkingRatio = dto.NegativeMarkingRatio,
                IsRandomOrder = dto.IsRandomOrder,
                Status = status,
                CreatedByAdminId = adminId,
                CreatedAt = DateTime.UtcNow
            };

            _db.PracticeTestTemplates.Add(template);
            await _db.SaveChangesAsync();

            if (dto.Questions.Count > 0)
            {
                var refs = dto.Questions.Select((r, i) => new PracticeTestTemplateQuestion
                {
                    PracticeTestTemplateId = template.Id,
                    QuestionId = r.QuestionId,
                    QuestionBankQuestionId = r.QuestionBankQuestionId,
                    QuestionOrder = i + 1
                });
                _db.PracticeTestTemplateQuestions.AddRange(refs);
                await _db.SaveChangesAsync();
            }

            await _audit.LogAsync(adminId, "PracticeTestTemplate.Create", "PracticeTestTemplate", template.Id);

            await _db.Entry(template).Reference(t => t.Subject).LoadAsync();
            await _db.Entry(template).Reference(t => t.Topic).LoadAsync();
            await _db.Entry(template).Reference(t => t.Exam).LoadAsync();
            await _db.Entry(template).Collection(t => t.Questions).LoadAsync();

            return Ok(ToAdminDto(template, 0));
        }

        // PUT /api/admin/practice-tests/{id} -- settings + (if provided) replaces the fixed question
        // list wholesale; pass an empty Questions array to convert a Curated template back to
        // FilterBased.
        [HttpPut("{id:guid}")]
        public async Task<ActionResult<PracticeTestTemplateAdminDto>> Update(Guid id, PracticeTestTemplateCreateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            var template = await _db.PracticeTestTemplates.Include(t => t.Questions).FirstOrDefaultAsync(t => t.Id == id);
            if (template == null) return NotFound();

            var validationError = await ValidateAsync(dto);
            if (validationError != null) return BadRequest(new { message = validationError });

            DifficultyLevel? difficulty = null;
            if (!string.IsNullOrWhiteSpace(dto.Difficulty) && Enum.TryParse<DifficultyLevel>(dto.Difficulty, true, out var d)) difficulty = d;

            template.Title = dto.Title.Trim();
            template.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            template.SubjectId = dto.SubjectId;
            template.TopicId = dto.TopicId;
            template.ExamId = dto.ExamId;
            template.YearFrom = dto.YearFrom;
            template.YearTo = dto.YearTo;
            template.Difficulty = difficulty;
            template.QuestionCount = dto.QuestionCount;
            template.DurationMinutes = dto.DurationMinutes;
            template.NegativeMarkingRatio = dto.NegativeMarkingRatio;
            template.IsRandomOrder = dto.IsRandomOrder;
            template.UpdatedAt = DateTime.UtcNow;

            _db.PracticeTestTemplateQuestions.RemoveRange(template.Questions);
            template.Questions.Clear();
            if (dto.Questions.Count > 0)
            {
                var refs = dto.Questions.Select((r, i) => new PracticeTestTemplateQuestion
                {
                    PracticeTestTemplateId = template.Id,
                    QuestionId = r.QuestionId,
                    QuestionBankQuestionId = r.QuestionBankQuestionId,
                    QuestionOrder = i + 1
                });
                foreach (var r in refs) template.Questions.Add(r);
            }

            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "PracticeTestTemplate.Update", "PracticeTestTemplate", id);

            await _db.Entry(template).Reference(t => t.Subject).LoadAsync();
            await _db.Entry(template).Reference(t => t.Topic).LoadAsync();
            await _db.Entry(template).Reference(t => t.Exam).LoadAsync();

            var attemptCount = await _db.StudentTestResults.CountAsync(r => r.PracticeTestTemplateId == id);
            return Ok(ToAdminDto(template, attemptCount));
        }

        // PATCH /api/admin/practice-tests/{id}/status
        [HttpPatch("{id:guid}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, UpdateTestStatusDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();
            if (!Enum.TryParse<TestPublishStatus>(dto.Status, true, out var status))
                return BadRequest(new { message = "Status must be Draft, Published, or Archived." });

            var template = await _db.PracticeTestTemplates.FindAsync(id);
            if (template == null) return NotFound();

            template.Status = status;
            template.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), $"PracticeTestTemplate.{status}", "PracticeTestTemplate", id);

            return Ok(new { template.Id, Status = template.Status.ToString() });
        }

        // GET /api/admin/practice-tests/{id}/attempts?page=&pageSize=
        [HttpGet("{id:guid}/attempts")]
        public async Task<ActionResult<PagedResult<object>>> GetAttempts(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.StudentTestResults.Include(r => r.User)
                .Where(r => r.PracticeTestTemplateId == id)
                .OrderByDescending(r => r.StartedAt);

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
                    r.StartedAt,
                    r.AttemptedAt
                }).ToListAsync();

            return Ok(new PagedResult<object> { Items = items.Cast<object>().ToList(), TotalCount = totalCount, Page = page, PageSize = pageSize });
        }

        // ---------- helpers ----------

        private async Task<string?> ValidateAsync(PracticeTestTemplateCreateDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Title)) return "Title is required.";
            if (dto.QuestionCount < 1 || dto.QuestionCount > 200) return "Number of Questions must be between 1 and 200.";
            if (dto.DurationMinutes < 1 || dto.DurationMinutes > 600) return "Duration must be between 1 and 600 minutes.";
            if (dto.NegativeMarkingRatio < 0 || dto.NegativeMarkingRatio > 1) return "Negative marking ratio must be between 0 and 1.";
            if (!string.IsNullOrWhiteSpace(dto.Difficulty) && !Enum.TryParse<DifficultyLevel>(dto.Difficulty, true, out _))
                return $"'{dto.Difficulty}' isn't a valid difficulty.";

            if (dto.Questions.Count > 0)
            {
                var questionIds = dto.Questions.Where(r => r.QuestionId.HasValue).Select(r => r.QuestionId!.Value).Distinct().ToList();
                var qbIds = dto.Questions.Where(r => r.QuestionBankQuestionId.HasValue).Select(r => r.QuestionBankQuestionId!.Value).Distinct().ToList();
                if (questionIds.Count > 0 && await _db.Questions.CountAsync(q => questionIds.Contains(q.Id)) != questionIds.Count)
                    return "One or more question ids don't exist.";
                if (qbIds.Count > 0 && await _db.QuestionBankQuestions.CountAsync(q => qbIds.Contains(q.Id) && q.IsActive) != qbIds.Count)
                    return "One or more Question Bank question ids don't exist.";
            }
            return null;
        }

        private static PracticeTestTemplateAdminDto ToAdminDto(PracticeTestTemplate t, int attemptCount) => new PracticeTestTemplateAdminDto
        {
            Id = t.Id,
            Title = t.Title,
            Description = t.Description,
            Subject = t.Subject?.Name,
            Topic = t.Topic?.Name,
            ExamName = t.Exam?.Name,
            YearFrom = t.YearFrom,
            YearTo = t.YearTo,
            Difficulty = t.Difficulty?.ToString(),
            QuestionCount = t.Questions.Count > 0 ? t.Questions.Count : t.QuestionCount,
            DurationMinutes = t.DurationMinutes,
            NegativeMarkingRatio = t.NegativeMarkingRatio,
            IsCurated = t.Questions.Count > 0,
            Status = t.Status.ToString(),
            CreatedByAdminName = t.CreatedByAdmin?.FullName ?? "Unknown",
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            AttemptCount = attemptCount
        };
    }
}
