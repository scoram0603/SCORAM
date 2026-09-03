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
    // Bulk-creates PAPER SHELLS (Exam+Year+Medium+Tier+Shift+Date+Code+Label, Status=Draft, zero
    // questions) from a CSV/Excel file -- one row = one paper. Deliberately a separate controller
    // from BulkImportController, which bulk-adds QUESTIONS into one already-chosen Draft paper: they
    // operate on different entities (Paper vs Question), and a single file here can span many
    // different exams at once (SSC CGL 2023, SSC CGL 2024, RRB NTPC 2023, ... all in one upload), so
    // it can't be nested under a single paperId route the way that one is.
    //
    // No ImportJob/rollback machinery here unlike BulkImportController. Each row's output is a whole,
    // independent Paper that's already visible and deletable via the existing "All Papers" list and
    // PapersController.Delete -- there's nothing a bulk "undo" would need to do that isn't already
    // covered, so Preview's rows are cached in memory only (no DB job row) and there's no Rollback
    // endpoint to match. If a row resolved to the wrong exam or has a typo, PapersController's new
    // identity-edit endpoint (PATCH /api/admin/papers/{id}/identity) fixes it in place; if it's
    // simply wrong, the admin deletes that one Draft paper like any other.
    [ApiController]
    [Route("api/admin/bulk-papers")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class BulkPaperImportController : ControllerBase
    {
        private const string CachePrefix = "bulk-paper-import-rows:";
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);

        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IBulkPaperImportService _importService;
        private readonly IMemoryCache _cache;
        private readonly IAuditLogService _audit;
        private readonly ILogger<BulkPaperImportController> _logger;

        public BulkPaperImportController(
            ScoramDbContext db, IAdminPermissionService permissions, IBulkPaperImportService importService,
            IMemoryCache cache, IAuditLogService audit, ILogger<BulkPaperImportController> logger)
        {
            _db = db;
            _permissions = permissions;
            _importService = importService;
            _cache = cache;
            _audit = audit;
            _logger = logger;
        }

        // POST /api/admin/bulk-papers/preview
        [HttpPost("preview")]
        [RequestSizeLimit(10 * 1024 * 1024)] // plain metadata rows, no images -- generous already at 10 MB
        public async Task<ActionResult<BulkPaperImportPreviewResponseDto>> Preview(IFormFile file)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper))
                return Forbid();

            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Attach a CSV or Excel (.xlsx) file." });

            var format = DetectFormat(file.FileName);
            if (format == null)
                return BadRequest(new { message = "Unrecognized file type -- expected .csv or .xlsx." });

            List<ImportedPaperRow> rows;
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
                _logger.LogWarning(ex, "Bulk paper import parse failure for {FileName}", file.FileName);
                return BadRequest(new { message = "Couldn't read that file. Double-check it matches the expected format and try again." });
            }

            if (rows.Count == 0)
                return BadRequest(new { message = "No paper rows found in the file." });

            await _importService.ValidateAsync(rows, _db);

            var jobId = Guid.NewGuid();
            _cache.Set(CachePrefix + jobId, rows, CacheLifetime);

            return Ok(new BulkPaperImportPreviewResponseDto
            {
                JobId = jobId,
                FileName = file.FileName,
                TotalRows = rows.Count,
                ValidCount = rows.Count(r => r.IsValid && !r.PaperAlreadyExists),
                InvalidCount = rows.Count(r => !r.IsValid),
                AlreadyExistsCount = rows.Count(r => r.IsValid && r.PaperAlreadyExists),
                Rows = rows
            });
        }

        // POST /api/admin/bulk-papers/{jobId}/commit -- creates a Draft Paper for every requested row
        // that's still valid and not a duplicate, both re-checked here rather than just trusted from
        // Preview (another admin could have created a colliding paper, or the same exam, in the
        // meantime). A row naming an exam that doesn't exist yet creates it (same as the single-paper
        // wizard's "+ New Exam" -- see PapersController.Create and ExamsController.
        // GetOrCreateExamCachedAsync), reusing that one new exam across every row in this batch that
        // names it rather than creating a duplicate per row.
        [HttpPost("{jobId:guid}/commit")]
        public async Task<ActionResult<BulkPaperImportCommitResultDto>> Commit(Guid jobId, BulkPaperImportCommitDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper))
                return Forbid();

            if (!_cache.TryGetValue(CachePrefix + jobId, out List<ImportedPaperRow>? rows) || rows == null)
                return BadRequest(new { message = "This preview has expired (previews last 30 minutes). Please re-upload the file." });

            var targetRows = dto.RowNumbers == null
                ? rows.Where(r => r.IsValid && !r.PaperAlreadyExists).ToList()
                : rows.Where(r => dto.RowNumbers.Contains(r.RowNumber)).ToList();

            var adminId = User.GetAdminId();
            var examCache = await _db.Exams.ToDictionaryAsync(e => e.Name, StringComparer.OrdinalIgnoreCase);

            // Which exams THIS batch has already given a paper to -- needed on top of the live
            // ExamHasContentAsync check below because a brand-new exam's first paper isn't saved to
            // the database until the single SaveChangesAsync at the end of this loop, so a second row
            // naming that same new exam would otherwise also see "no content yet" and wrongly qualify
            // for Paper.ExamCreatedForThisPaper too -- only the row that's genuinely alone on its exam
            // should get that flag.
            var examsGivenAPaperThisBatch = new HashSet<Guid>();

            var created = new List<Paper>();
            var skippedExisting = 0;

            foreach (var row in targetRows)
            {
                if (!row.IsValid) continue; // never force-commit a row flagged invalid, even if explicitly requested by row number

                var exam = await ExamsController.GetOrCreateExamCachedAsync(_db, row.ExamName.Trim(), adminId, examCache);
                var language = Enum.Parse<PaperLanguage>(row.Medium, ignoreCase: true);

                var existing = await _db.Papers.FirstOrDefaultAsync(p =>
                    p.ExamId == exam.Id && p.Year == row.Year && p.Language == language &&
                    p.PaperCode == row.PaperCode && p.Tier == row.Tier &&
                    p.ExamDate == row.ExamDate && p.Shift == row.Shift && p.PaperLabel == row.PaperLabel);

                if (existing != null)
                {
                    skippedExisting++;
                    continue;
                }

                var examWasEmpty = !examsGivenAPaperThisBatch.Contains(exam.Id)
                    && !await ExamsController.ExamHasContentAsync(_db, exam.Id, exam.Name);
                examsGivenAPaperThisBatch.Add(exam.Id);

                var paper = new Paper
                {
                    ExamId = exam.Id,
                    Year = row.Year,
                    Language = language,
                    PaperCode = row.PaperCode,
                    Tier = row.Tier,
                    ExamDate = row.ExamDate,
                    Shift = row.Shift,
                    PaperLabel = row.PaperLabel,
                    Status = PaperStatus.Draft,
                    ExamCreatedForThisPaper = examWasEmpty,
                    CreatedByAdminId = adminId,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Papers.Add(paper);
                created.Add(paper);
            }

            await _db.SaveChangesAsync();
            _cache.Remove(CachePrefix + jobId);

            await _audit.LogAsync(adminId, "BulkPaperImport.Commit", "Paper", null,
                $"{created.Count} paper shell(s) created, {skippedExisting} already existed and were skipped");

            var createdDtos = new List<PaperResponseDto>();
            foreach (var p in created)
            {
                await _db.Entry(p).Reference(x => x.Exam).LoadAsync();
                await _db.Entry(p).Reference(x => x.CreatedByAdmin).LoadAsync();
                createdDtos.Add(PapersController.MapToDto(p, questionCountOverride: 0));
            }

            return Ok(new BulkPaperImportCommitResultDto
            {
                CreatedCount = created.Count,
                SkippedExistingCount = skippedExisting,
                CreatedPapers = createdDtos
            });
        }

        private static ImportFileFormat? DetectFormat(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".csv" => ImportFileFormat.Csv,
                ".xlsx" => ImportFileFormat.Excel,
                _ => null
            };
        }
    }
}
