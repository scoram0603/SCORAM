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
    // covered, so Preview's rows are cached (see IStagedDataCache -- Redis-backed when configured, so
    // this survives landing on a different app instance between preview and commit, same reasoning as
    // BulkImportController's equivalent) rather than a DB job row, and there's no Rollback endpoint to
    // match. If a row resolved to the wrong exam or has a typo, PapersController's new identity-edit
    // endpoint (PATCH /api/admin/papers/{id}/identity) fixes it in place; if it's simply wrong, the
    // admin deletes that one Draft paper like any other.
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
        private readonly IStagedDataCache _cache;
        private readonly ILogger<BulkPaperImportController> _logger;
        private readonly IBackgroundJobQueue _jobQueue;
        private readonly IBulkPaperImportCommitService _commitService;

        public BulkPaperImportController(
            ScoramDbContext db, IAdminPermissionService permissions, IBulkPaperImportService importService,
            IStagedDataCache cache, ILogger<BulkPaperImportController> logger,
            IBackgroundJobQueue jobQueue, IBulkPaperImportCommitService commitService)
        {
            _db = db;
            _permissions = permissions;
            _importService = importService;
            _cache = cache;
            _logger = logger;
            _jobQueue = jobQueue;
            _commitService = commitService;
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
                return BadRequest(new { message = "Unrecognized file type -- expected .csv, .xlsx, or .json." });

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
            await _cache.SetAsync(CachePrefix + jobId, rows, CacheLifetime);

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

            var hasRows = await _cache.GetAsync<List<ImportedPaperRow>>(CachePrefix + jobId) != null;
            if (!hasRows)
                return BadRequest(new { message = "This preview has expired (previews last 30 minutes). Please re-upload the file." });

            var adminId = User.GetAdminId();

            if (_jobQueue.IsAvailable)
            {
                await _cache.SetAsync(BulkPaperImportCommitWorker.StatusCachePrefix + jobId,
                    new BulkPaperImportCommitStatus { Status = "Processing" }, TimeSpan.FromHours(2));
                await _jobQueue.EnqueueBulkPaperImportCommitJobAsync(new BulkPaperImportCommitJob { JobId = jobId, AdminId = adminId, RowNumbers = dto.RowNumbers });

                // 202: accepted, not yet done -- the frontend polls GET {jobId}/commit-status (added
                // below) until Status moves past "Processing". Unlike BulkImportController, there's
                // no existing DB-backed GetStatus endpoint to piggyback on here (see
                // BulkPaperImportCommitJob's own comment on why), hence the new endpoint.
                return Accepted($"/api/admin/bulk-papers/{jobId}/commit-status", new BulkPaperImportCommitResultDto { Status = "Processing" });
            }

            try
            {
                var result = await _commitService.CommitAsync(jobId, adminId, dto.RowNumbers);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // GET /api/admin/bulk-papers/{jobId}/commit-status -- poll this after Commit comes back 202.
        [HttpGet("{jobId:guid}/commit-status")]
        public async Task<ActionResult<BulkPaperImportCommitStatus>> GetCommitStatus(Guid jobId)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper))
                return Forbid();

            var status = await _cache.GetAsync<BulkPaperImportCommitStatus>(BulkPaperImportCommitWorker.StatusCachePrefix + jobId);
            if (status == null)
                return NotFound(new { message = "No commit status found for this job (it may have expired, or Commit was never called with the background queue active)." });

            return Ok(status);
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
    }
}
