using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Bulk question import: CSV/Excel/JSON only (text fields -- no bulk image import yet; a question's
    // images can still be added afterward one at a time via the normal edit form). Two-step flow:
    // preview (parse + validate, nothing written to Questions yet) then commit (writes the rows the
    // admin actually confirms). Only ever targets a Draft paper -- same rule as the one-by-one
    // QuestionsController.Create.
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class BulkImportController : ControllerBase
    {
        // Preview rows live here between preview and commit, keyed by ImportJob.Id -- see the
        // "deliberate simplicity tradeoff" note on Models/ImportJob.cs for what this does and doesn't
        // survive (an app restart loses any in-progress review).
        private const string CachePrefix = "bulk-import-rows:";
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);

        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IBulkImportService _importService;
        private readonly IMemoryCache _cache;
        private readonly IAuditLogService _audit;
        private readonly ILogger<BulkImportController> _logger;
        private readonly IQuestionBankMirrorService _mirror;

        public BulkImportController(
            ScoramDbContext db, IAdminPermissionService permissions, IBulkImportService importService,
            IMemoryCache cache, IAuditLogService audit, ILogger<BulkImportController> logger,
            IQuestionBankMirrorService mirror)
        {
            _db = db;
            _permissions = permissions;
            _importService = importService;
            _cache = cache;
            _audit = audit;
            _logger = logger;
            _mirror = mirror;
        }

        // POST /api/admin/papers/{paperId}/bulk-import/preview  (multipart/form-data, field name "file")
        [HttpPost("papers/{paperId:guid}/bulk-import/preview")]
        [RequestSizeLimit(20 * 1024 * 1024)] // 20 MB -- generous for a text-only spreadsheet/JSON file
        public async Task<ActionResult<BulkImportPreviewResponseDto>> Preview(Guid paperId, IFormFile file)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper))
                return Forbid();

            var paper = await _db.Papers.FindAsync(paperId);
            if (paper == null) return NotFound(new { message = "Paper not found." });
            if (paper.Status != PaperStatus.Draft)
                return BadRequest(new { message = "Questions can only be bulk-imported into a Draft paper." });

            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Attach a CSV, Excel (.xlsx), or JSON file." });

            var format = DetectFormat(file.FileName);
            if (format == null)
                return BadRequest(new { message = "Unrecognized file type -- expected .csv, .xlsx, or .json." });

            List<ImportedQuestionRow> rows;
            try
            {
                await using var stream = file.OpenReadStream();
                rows = await _importService.ParseAsync(stream, format.Value);
            }
            catch (InvalidDataException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bulk import parse failure for {FileName}", file.FileName);
                return BadRequest(new { message = "Couldn't read that file. Double-check it matches the expected format and try again." });
            }

            if (rows.Count == 0)
                return BadRequest(new { message = "No question rows found in the file." });

            var existingQuestions = await _db.Questions.Where(q => q.PaperId == paperId).ToListAsync();
            _importService.Validate(rows, existingQuestions);

            var job = new ImportJob
            {
                PaperId = paperId,
                CreatedByAdminId = User.GetAdminId(),
                FileName = file.FileName,
                Format = format.Value,
                Status = ImportJobStatus.PendingReview,
                TotalRows = rows.Count,
                ValidRows = rows.Count(r => r.IsValid),
                InvalidRows = rows.Count(r => !r.IsValid)
            };
            _db.ImportJobs.Add(job);
            await _db.SaveChangesAsync();

            _cache.Set(CachePrefix + job.Id, rows, CacheLifetime);

            return Ok(new BulkImportPreviewResponseDto
            {
                JobId = job.Id,
                FileName = job.FileName,
                Format = job.Format.ToString(),
                TotalRows = job.TotalRows,
                ValidCount = job.ValidRows,
                InvalidCount = job.InvalidRows,
                Rows = rows
            });
        }

        // POST /api/admin/bulk-import/{jobId}/commit
        [HttpPost("bulk-import/{jobId:guid}/commit")]
        public async Task<ActionResult<BulkImportCommitResultDto>> Commit(Guid jobId, BulkImportCommitDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper))
                return Forbid();

            var job = await _db.ImportJobs.Include(j => j.Paper).FirstOrDefaultAsync(j => j.Id == jobId);
            if (job == null) return NotFound(new { message = "Import job not found." });
            if (job.Status != ImportJobStatus.PendingReview)
                return BadRequest(new { message = $"This import is already {job.Status} and can't be committed again." });
            if (job.Paper == null || job.Paper.Status != PaperStatus.Draft)
                return BadRequest(new { message = "The paper is no longer in Draft -- can't commit into it." });

            if (!_cache.TryGetValue(CachePrefix + jobId, out List<ImportedQuestionRow>? rows) || rows == null)
                return BadRequest(new { message = "This preview has expired (previews last 30 minutes). Please re-upload the file." });

            var wanted = dto.RowNumbers != null ? new HashSet<int>(dto.RowNumbers) : null;
            var toCommit = rows.Where(r => r.IsValid && (wanted == null || wanted.Contains(r.RowNumber))).ToList();
            var skipped = rows.Count - toCommit.Count;

            var adminId = User.GetAdminId();
            var createdQuestions = new List<Question>();
            foreach (var row in toCommit)
            {
                var question = new Question
                {
                    PaperId = job.PaperId,
                    QuestionNumber = row.QuestionNumber,
                    Subject = row.Subject,
                    Topic = row.Topic,
                    DifficultyLevel = Enum.Parse<DifficultyLevel>(row.DifficultyLevel, ignoreCase: true),
                    QuestionText = row.QuestionText,
                    OptionA = row.OptionA,
                    OptionB = row.OptionB,
                    OptionC = row.OptionC,
                    OptionD = row.OptionD,
                    CorrectOption = Enum.Parse<OptionLetter>(row.CorrectOption, ignoreCase: true),
                    Explanation = row.Explanation,
                    SourceReference = row.SourceReference,
                    CreatedByAdminId = adminId,
                    ImportJobId = job.Id,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Questions.Add(question);
                createdQuestions.Add(question);
            }

            job.Status = ImportJobStatus.Committed;
            job.ImportedCount = toCommit.Count;
            job.CommittedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            // Auto-mirror every newly-imported PYQ question into the Question Bank (see
            // IQuestionBankMirrorService) -- same reasoning as QuestionsController.Create: a bulk
            // import is just as much "a PYQ upload" as the one-by-one form, and bulk-imported
            // questions have no images to lose in the mirror (this flow doesn't support images at
            // all -- see this controller's own class comment), so nothing is lost in translation here.
            foreach (var question in createdQuestions)
            {
                var mirrorId = await _mirror.MirrorFromPyqAsync(_db, question, job.Paper.ExamId, job.Paper.Year, adminId);
                if (mirrorId.HasValue) question.MirroredToQuestionBankQuestionId = mirrorId;
            }
            try { await _db.SaveChangesAsync(); } catch { /* non-critical, see MirrorFromPyqAsync's own comment */ }

            _cache.Remove(CachePrefix + jobId);
            await _audit.LogAsync(adminId, "BulkImport.Commit", "Paper", job.PaperId, $"{toCommit.Count} question(s) imported from {job.FileName}");

            return Ok(new BulkImportCommitResultDto
            {
                JobId = job.Id,
                Status = job.Status.ToString(),
                ImportedCount = toCommit.Count,
                SkippedCount = skipped
            });
        }

        // GET /api/admin/bulk-import/{jobId}
        [HttpGet("bulk-import/{jobId:guid}")]
        public async Task<ActionResult<ImportJobResponseDto>> GetStatus(Guid jobId)
        {
            var job = await _db.ImportJobs.Include(j => j.Paper).ThenInclude(p => p!.Exam).Include(j => j.CreatedByAdmin)
                .FirstOrDefaultAsync(j => j.Id == jobId);
            if (job == null) return NotFound();

            return Ok(ToResponseDto(job));
        }

        // GET /api/admin/bulk-import/history?paperId=&page=&pageSize=
        [HttpGet("bulk-import/history")]
        public async Task<ActionResult<PagedResult<ImportJobResponseDto>>> History(
            [FromQuery] Guid? paperId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.ImportJobs.Include(j => j.Paper).ThenInclude(p => p!.Exam).Include(j => j.CreatedByAdmin).AsQueryable();
            if (paperId.HasValue) query = query.Where(j => j.PaperId == paperId.Value);
            query = query.OrderByDescending(j => j.CreatedAt);

            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Ok(new PagedResult<ImportJobResponseDto>
            {
                Items = items.Select(ToResponseDto).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        // POST /api/admin/bulk-import/{jobId}/rollback -- deletes exactly the questions this import
        // created (tagged via Question.ImportJobId), leaving anything entered by hand untouched. Only
        // allowed while the paper is still Draft, same reasoning as PapersController.Delete: once a
        // paper's been sent for review or published, removing questions out from under it needs the
        // normal edit/delete flow, not a bulk undo.
        [HttpPost("bulk-import/{jobId:guid}/rollback")]
        public async Task<IActionResult> Rollback(Guid jobId)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.DeletePaper))
                return Forbid();

            var job = await _db.ImportJobs.Include(j => j.Paper).FirstOrDefaultAsync(j => j.Id == jobId);
            if (job == null) return NotFound();
            if (job.Status != ImportJobStatus.Committed)
                return BadRequest(new { message = "Only a committed import can be rolled back." });
            if (job.Paper == null || job.Paper.Status != PaperStatus.Draft)
                return BadRequest(new { message = "The paper is no longer Draft -- roll back individual questions by hand instead." });

            var imported = await _db.Questions.Where(q => q.ImportJobId == jobId).ToListAsync();
            _db.Questions.RemoveRange(imported);

            job.Status = ImportJobStatus.RolledBack;
            job.RolledBackAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "BulkImport.Rollback", "Paper", job.PaperId, $"{imported.Count} question(s) removed from {job.FileName}");

            return NoContent();
        }

        private static ImportFileFormat? DetectFormat(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".csv" => ImportFileFormat.Csv,
                ".xlsx" => ImportFileFormat.Excel,
                ".json" => ImportFileFormat.Json,
                _ => null
            };
        }

        private static ImportJobResponseDto ToResponseDto(ImportJob j) => new ImportJobResponseDto
        {
            Id = j.Id,
            PaperId = j.PaperId,
            ExamName = j.Paper?.Exam?.Name ?? "Unknown",
            Year = j.Paper?.Year ?? 0,
            FileName = j.FileName,
            Format = j.Format.ToString(),
            Status = j.Status.ToString(),
            TotalRows = j.TotalRows,
            ValidRows = j.ValidRows,
            InvalidRows = j.InvalidRows,
            ImportedCount = j.ImportedCount,
            CreatedByAdminName = j.CreatedByAdmin?.FullName ?? "Unknown",
            CreatedAt = j.CreatedAt,
            CommittedAt = j.CommittedAt,
            RolledBackAt = j.RolledBackAt
        };
    }
}
