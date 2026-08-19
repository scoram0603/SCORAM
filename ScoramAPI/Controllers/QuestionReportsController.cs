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
    // "Report Question" (spec section 28-A). The QuestionReport model/table already existed before
    // this feature (it was counted on the admin dashboard) but had no controller -- this is the first
    // real implementation, built to work for BOTH the legacy Paper-based Question and the new
    // QuestionBankQuestion from day one (section 28-E), rather than a Question Bank-only version.
    [ApiController]
    [Route("api")]
    public class QuestionReportsController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IAuditLogService _audit;

        public QuestionReportsController(ScoramDbContext db, IAdminPermissionService permissions, IAuditLogService audit)
        {
            _db = db;
            _permissions = permissions;
            _audit = audit;
        }

        // POST /api/questions/{questionId}/reports
        [HttpPost("questions/{questionId:guid}/reports")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<QuestionReportResponseDto>> CreateForQuestion(Guid questionId, QuestionReportCreateDto dto)
        {
            var exists = await _db.Questions.AnyAsync(q => q.Id == questionId);
            if (!exists) return NotFound(new { message = "Question not found." });

            return await CreateReportAsync(dto, r => r.QuestionId = questionId);
        }

        // POST /api/question-bank/{questionId}/reports
        [HttpPost("question-bank/{questionId:guid}/reports")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<QuestionReportResponseDto>> CreateForQuestionBank(Guid questionId, QuestionReportCreateDto dto)
        {
            var exists = await _db.QuestionBankQuestions.AnyAsync(q => q.Id == questionId && q.IsActive);
            if (!exists) return NotFound(new { message = "Question not found." });

            return await CreateReportAsync(dto, r => r.QuestionBankQuestionId = questionId);
        }

        private async Task<ActionResult<QuestionReportResponseDto>> CreateReportAsync(QuestionReportCreateDto dto, Action<QuestionReport> assignQuestion)
        {
            if (!Enum.TryParse<ReportType>(dto.ReportType, ignoreCase: true, out var reportType))
                return BadRequest(new { message = $"'{dto.ReportType}' isn't a recognized report reason." });

            var report = new QuestionReport
            {
                ReportedByUserId = User.GetUserId(),
                ReportType = reportType,
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                ProofUrl = dto.ProofUrl,
                Status = ReportStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
            assignQuestion(report);

            _db.QuestionReports.Add(report);
            await _db.SaveChangesAsync();

            return Ok(new QuestionReportResponseDto
            {
                Id = report.Id,
                QuestionId = report.QuestionId,
                QuestionBankQuestionId = report.QuestionBankQuestionId,
                ReportType = report.ReportType.ToString(),
                Description = report.Description,
                Status = report.Status.ToString(),
                CreatedAt = report.CreatedAt
            });
        }

        // GET /api/admin/reports/pending?page=&pageSize= -- moderation queue for both question types,
        // oldest first (same "nothing sits forgotten" ordering as the Solutions queue).
        [HttpGet("admin/reports/pending")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<PagedResult<AdminQuestionReportDto>>> GetPending([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateQuestionReports))
                return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.QuestionReports
                .Include(r => r.ReportedByUser)
                .Include(r => r.Question).ThenInclude(q => q!.Paper).ThenInclude(p => p!.Exam)
                .Include(r => r.QuestionBankQuestion).ThenInclude(q => q!.Subject)
                .Include(r => r.QuestionBankQuestion).ThenInclude(q => q!.Topic)
                .Where(r => r.Status == ReportStatus.Pending)
                .OrderBy(r => r.CreatedAt);

            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Ok(new PagedResult<AdminQuestionReportDto>
            {
                Items = items.Select(ToAdminDto).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        // PATCH /api/admin/reports/{id}/status  { "status": "UnderReview" | "Resolved" | "Rejected" }
        [HttpPatch("admin/reports/{id:guid}/status")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<IActionResult> UpdateStatus(Guid id, UpdateReportStatusDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateQuestionReports))
                return Forbid();

            if (!Enum.TryParse<ReportStatus>(dto.Status, ignoreCase: true, out var status) || status == ReportStatus.Pending)
                return BadRequest(new { message = "Status must be UnderReview, Resolved, or Rejected." });

            var report = await _db.QuestionReports.FindAsync(id);
            if (report == null) return NotFound();

            report.Status = status;
            if (status is ReportStatus.Resolved or ReportStatus.Rejected)
                report.ResolvedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), $"QuestionReport.{status}", "QuestionReport", id);

            return Ok(new { report.Id, Status = report.Status.ToString() });
        }

        private static AdminQuestionReportDto ToAdminDto(QuestionReport r)
        {
            var isQuestionBank = r.QuestionBankQuestionId != null;
            string snippet;
            string contextLabel;

            if (isQuestionBank && r.QuestionBankQuestion != null)
            {
                var q = r.QuestionBankQuestion;
                snippet = q.QuestionText.Length > 140 ? q.QuestionText[..140] + "…" : q.QuestionText;
                contextLabel = $"{q.Subject?.Name ?? "Unknown"} / {q.Topic?.Name ?? "Unknown"}";
            }
            else if (r.Question != null)
            {
                var q = r.Question;
                snippet = q.QuestionText.Length > 140 ? q.QuestionText[..140] + "…" : q.QuestionText;
                contextLabel = q.Paper?.Exam?.Name ?? q.ExamName ?? "Unknown";
            }
            else
            {
                snippet = "(question no longer exists)";
                contextLabel = "Unknown";
            }

            return new AdminQuestionReportDto
            {
                Id = r.Id,
                QuestionId = r.QuestionId,
                QuestionBankQuestionId = r.QuestionBankQuestionId,
                IsQuestionBank = isQuestionBank,
                QuestionTextSnippet = snippet,
                ContextLabel = contextLabel,
                ReportType = r.ReportType.ToString(),
                Description = r.Description,
                ProofUrl = r.ProofUrl,
                Status = r.Status.ToString(),
                ReportedByName = r.ReportedByUser?.FullName ?? "Unknown",
                CreatedAt = r.CreatedAt,
                ResolvedAt = r.ResolvedAt
            };
        }
    }
}
