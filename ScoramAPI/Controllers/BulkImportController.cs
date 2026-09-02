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
    // Bulk question import: CSV/Excel/JSON/ZIP. CSV/Excel/JSON are text-only (a question's images can
    // still be added afterward one at a time via the normal edit form); a ZIP package
    // (questions.json + images/ + optional metadata.json, see spec section 51) additionally supports
    // per-question images and an optional rich ContentBlocks sequence -- see
    // Services/BulkUploadZipService.cs and IBulkImportService.ParseZipAsync. Two-step flow: preview
    // (parse + validate, nothing written to Questions yet) then commit (writes the rows the admin
    // actually confirms). Only ever targets a Draft paper -- same rule as the one-by-one
    // QuestionsController.Create.
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class BulkImportController : ControllerBase
    {
        // Preview rows live here between preview and commit, keyed by ImportJob.Id -- see the
        // "deliberate simplicity tradeoff" note on Models/ImportJob.cs for what this does and doesn't
        // survive (an app restart loses any in-progress review). A ZIP upload's staged images live in
        // Azure Blob Storage under "bulk-import-staging/{jobId}/" for the same window -- cleaned up
        // explicitly at the end of Commit(), and by BulkImportStagingCleanupService for anything
        // abandoned past that window.
        private const string CachePrefix = "bulk-import-rows:";
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);

        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IBulkImportService _importService;
        private readonly IBulkUploadZipService _zipService;
        private readonly IFileStorageService _fileStorage;
        private readonly IMemoryCache _cache;
        private readonly IAuditLogService _audit;
        private readonly ILogger<BulkImportController> _logger;
        private readonly IQuestionBankMirrorService _mirror;
        private readonly IInstantSearchService _instantSearch;

        public BulkImportController(
            ScoramDbContext db, IAdminPermissionService permissions, IBulkImportService importService,
            IBulkUploadZipService zipService, IFileStorageService fileStorage,
            IMemoryCache cache, IAuditLogService audit, ILogger<BulkImportController> logger,
            IQuestionBankMirrorService mirror, IInstantSearchService instantSearch)
        {
            _db = db;
            _permissions = permissions;
            _importService = importService;
            _zipService = zipService;
            _fileStorage = fileStorage;
            _cache = cache;
            _audit = audit;
            _logger = logger;
            _mirror = mirror;
            _instantSearch = instantSearch;
        }

        // POST /api/admin/papers/{paperId}/bulk-import/preview  (multipart/form-data, field name "file")
        // Format is auto-detected from the file extension (.csv/.xlsx/.json/.zip) -- one endpoint for
        // all four, same as before ZIP support existed for the other three.
        [HttpPost("papers/{paperId:guid}/bulk-import/preview")]
        [RequestSizeLimit(250 * 1024 * 1024)] // 250 MB -- generous for a ZIP full of question images; plain CSV/Excel/JSON are tiny in comparison
        public async Task<ActionResult<BulkImportPreviewResponseDto>> Preview(Guid paperId, IFormFile file)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper))
                return Forbid();

            var paper = await _db.Papers.FindAsync(paperId);
            if (paper == null) return NotFound(new { message = "Paper not found." });
            if (paper.Status != PaperStatus.Draft)
                return BadRequest(new { message = "Questions can only be bulk-imported into a Draft paper." });

            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Attach a CSV, Excel (.xlsx), JSON, or ZIP file." });

            var format = DetectFormat(file.FileName);
            if (format == null)
                return BadRequest(new { message = "Unrecognized file type -- expected .csv, .xlsx, .json, or .zip." });

            // A ZIP upload's images get staged under this id -- generated up front (rather than
            // waiting for the ImportJob row below) so the very first staged image and the eventual
            // ImportJob.Id agree, letting Commit()/cleanup find them later by job id alone.
            var stagingId = Guid.NewGuid();

            List<ImportedQuestionRow> rows;
            try
            {
                if (format == ImportFileFormat.Zip)
                {
                    await using var zipStream = file.OpenReadStream();
                    var contents = _zipService.Extract(zipStream);
                    rows = await _importService.ParseZipAsync(contents.QuestionsJson, contents.Images, $"bulk-import-staging/{stagingId}");
                }
                else
                {
                    await using var stream = file.OpenReadStream();
                    rows = await _importService.ParseAsync(stream, format.Value);
                }
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
                Id = stagingId,
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

        // PATCH /api/admin/bulk-import/{jobId}/rows/{rowNumber} -- admin corrects a row's text,
        // options, correct answer, or explanation during review, before commit (spec section 10: the
        // preview needs to let a mistake be fixed right there instead of forcing a re-upload).
        // Overwrites the cached row and re-runs full validation (duplicate Q.No checks depend on
        // sibling rows, not just this one), so the response's IsValid/Errors reflect the edit
        // immediately -- the same shape Preview returns, so the frontend can just swap the row in.
        [HttpPatch("bulk-import/{jobId:guid}/rows/{rowNumber:int}")]
        public async Task<ActionResult<ImportedQuestionRow>> UpdateRow(Guid jobId, int rowNumber, ImportedQuestionRow edited)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper))
                return Forbid();

            var job = await _db.ImportJobs.Include(j => j.Paper).FirstOrDefaultAsync(j => j.Id == jobId);
            if (job == null) return NotFound(new { message = "Import job not found." });
            if (job.Status != ImportJobStatus.PendingReview)
                return BadRequest(new { message = $"This import is already {job.Status} and its rows can't be edited anymore." });
            if (job.Paper == null || job.Paper.Status != PaperStatus.Draft)
                return BadRequest(new { message = "The paper is no longer in Draft." });

            if (!_cache.TryGetValue(CachePrefix + jobId, out List<ImportedQuestionRow>? rows) || rows == null)
                return BadRequest(new { message = "This preview has expired (previews last 30 minutes). Please re-upload the file." });

            var row = rows.FirstOrDefault(r => r.RowNumber == rowNumber);
            if (row == null) return NotFound(new { message = "Row not found in this import." });

            // Only the editable fields -- RowNumber/IsValid/Errors are never trusted from the client,
            // they're recomputed by Validate() below.
            row.QuestionNumber = edited.QuestionNumber;
            row.Subject = edited.Subject;
            row.Topic = edited.Topic;
            row.DifficultyLevel = edited.DifficultyLevel;
            row.QuestionText = edited.QuestionText;
            row.OptionA = edited.OptionA;
            row.OptionB = edited.OptionB;
            row.OptionC = edited.OptionC;
            row.OptionD = edited.OptionD;
            row.CorrectOption = edited.CorrectOption;
            row.Explanation = edited.Explanation;
            row.SourceReference = edited.SourceReference;

            var existingQuestions = await _db.Questions.Where(q => q.PaperId == job.PaperId).ToListAsync();
            _importService.Validate(rows, existingQuestions);

            job.ValidRows = rows.Count(r => r.IsValid);
            job.InvalidRows = rows.Count(r => !r.IsValid);
            _cache.Set(CachePrefix + jobId, rows, CacheLifetime);
            await _db.SaveChangesAsync();

            return Ok(row);
        }

        // POST /api/admin/bulk-import/{jobId}/rows/{rowNumber}/images -- admin adds, replaces, or
        // removes one or more of this row's images during preview, before commit. Closes the gap
        // where only a ZIP upload's own images/ folder could ever populate a row's images: this
        // works for a row from ANY format (CSV/Excel/JSON/ZIP) -- a CSV-sourced row that had no
        // images at all can get its first one added right here, same as a ZIP row can have its
        // staged image replaced or removed. Images land in the same "bulk-import-staging/{jobId}"
        // folder a ZIP upload's own images use, so Commit()'s existing copy-then-cleanup logic
        // handles these identically either way -- nothing else needed to wire this in.
        [HttpPost("bulk-import/{jobId:guid}/rows/{rowNumber:int}/images")]
        [RequestSizeLimit(30 * 1024 * 1024)] // a handful of images for one row, not a whole file
        public async Task<ActionResult<ImportedQuestionRow>> UpdateRowImages(Guid jobId, int rowNumber, [FromForm] BulkImportRowImagesDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper))
                return Forbid();

            var job = await _db.ImportJobs.Include(j => j.Paper).FirstOrDefaultAsync(j => j.Id == jobId);
            if (job == null) return NotFound(new { message = "Import job not found." });
            if (job.Status != ImportJobStatus.PendingReview)
                return BadRequest(new { message = $"This import is already {job.Status} and its rows can't be edited anymore." });
            if (job.Paper == null || job.Paper.Status != PaperStatus.Draft)
                return BadRequest(new { message = "The paper is no longer in Draft." });

            if (!_cache.TryGetValue(CachePrefix + jobId, out List<ImportedQuestionRow>? rows) || rows == null)
                return BadRequest(new { message = "This preview has expired (previews last 30 minutes). Please re-upload the file." });

            var row = rows.FirstOrDefault(r => r.RowNumber == rowNumber);
            if (row == null) return NotFound(new { message = "Row not found in this import." });

            var stagingSubfolder = $"bulk-import-staging/{jobId}";
            try
            {
                row.QuestionImageUrl = await ApplyStagedImageUpdate(dto.QuestionImage, dto.RemoveQuestionImage, row.QuestionImageUrl, stagingSubfolder);
                row.OptionAImageUrl = await ApplyStagedImageUpdate(dto.OptionAImage, dto.RemoveOptionAImage, row.OptionAImageUrl, stagingSubfolder);
                row.OptionBImageUrl = await ApplyStagedImageUpdate(dto.OptionBImage, dto.RemoveOptionBImage, row.OptionBImageUrl, stagingSubfolder);
                row.OptionCImageUrl = await ApplyStagedImageUpdate(dto.OptionCImage, dto.RemoveOptionCImage, row.OptionCImageUrl, stagingSubfolder);
                row.OptionDImageUrl = await ApplyStagedImageUpdate(dto.OptionDImage, dto.RemoveOptionDImage, row.OptionDImageUrl, stagingSubfolder);
                row.ExplanationImageUrl = await ApplyStagedImageUpdate(dto.ExplanationImage, dto.RemoveExplanationImage, row.ExplanationImageUrl, stagingSubfolder);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            // This is a direct, confirmed admin action -- not a re-run of ZIP staging -- so it takes
            // priority over whatever ImageErrors this row may have carried from its original upload
            // (e.g. "image not found in ZIP" for a filename the admin has now fixed by hand here).
            // Cleared wholesale rather than field-by-field since ImageErrors are free-text messages,
            // not structured per-field state.
            row.ImageErrors.Clear();

            var existingQuestions = await _db.Questions.Where(q => q.PaperId == job.PaperId).ToListAsync();
            _importService.Validate(rows, existingQuestions);

            job.ValidRows = rows.Count(r => r.IsValid);
            job.InvalidRows = rows.Count(r => !r.IsValid);
            _cache.Set(CachePrefix + jobId, rows, CacheLifetime);
            await _db.SaveChangesAsync();

            return Ok(row);
        }

        // Same "upload new before deleting old" ordering as QuestionsController.ApplyImageUpdate
        // (spec section 41: never delete an old image before the new one is safely stored) -- the
        // only difference is a caller-supplied subfolder, since this saves into a job's staging
        // folder rather than always "question-images".
        private async Task<string?> ApplyStagedImageUpdate(IFormFile? newFile, bool remove, string? currentUrl, string stagingSubfolder)
        {
            if (newFile != null)
            {
                var newUrl = await _fileStorage.SaveImageAsync(newFile, stagingSubfolder);
                await _fileStorage.DeleteImageAsync(currentUrl);
                return newUrl;
            }
            if (remove)
            {
                await _fileStorage.DeleteImageAsync(currentUrl);
                return null;
            }
            return currentUrl;
        }

        // GET /api/admin/bulk-import/{jobId}/questions -- the actual Question rows a *committed*
        // import created (via Question.ImportJobId), for the "Recent imports" history view: an admin
        // can open a past batch and fix a question in it without hunting through the paper's full
        // Q.1..Q.N list. Editing itself reuses the normal QuestionsController.Update endpoint (same
        // Draft-or-PendingReview rule as everywhere else -- nothing job-specific to enforce here).
        [HttpGet("bulk-import/{jobId:guid}/questions")]
        public async Task<ActionResult<List<QuestionDetailDto>>> GetJobQuestions(Guid jobId)
        {
            var job = await _db.ImportJobs.FindAsync(jobId);
            if (job == null) return NotFound(new { message = "Import job not found." });

            var questions = await _db.Questions
                .Where(q => q.ImportJobId == jobId)
                .OrderBy(q => q.QuestionNumber)
                .ToListAsync();

            return Ok(questions.Select(QuestionsController.MapToDetailDto).ToList());
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
                    ContentBlocksJson = row.ContentBlocksJson,
                    CreatedByAdminId = adminId,
                    ImportJobId = job.Id,
                    CreatedAt = DateTime.UtcNow
                };

                // Any images were staged (ZIP upload only -- see Preview) under
                // "bulk-import-staging/{jobId}"; copy each one into the permanent "question-images"
                // folder rather than pointing the question straight at the staging blob, since the
                // whole staging folder gets deleted below once this loop finishes. CopyImageAsync
                // (already used by QuestionBankMirrorService for the same "needs its own independent
                // copy" reason) no-ops to null for a null source, so this is safe to call
                // unconditionally even for a non-ZIP import where these are all null.
                question.QuestionImageUrl = await _fileStorage.CopyImageAsync(row.QuestionImageUrl, "question-images");
                question.OptionAImageUrl = await _fileStorage.CopyImageAsync(row.OptionAImageUrl, "question-images");
                question.OptionBImageUrl = await _fileStorage.CopyImageAsync(row.OptionBImageUrl, "question-images");
                question.OptionCImageUrl = await _fileStorage.CopyImageAsync(row.OptionCImageUrl, "question-images");
                question.OptionDImageUrl = await _fileStorage.CopyImageAsync(row.OptionDImageUrl, "question-images");
                question.ExplanationImageUrl = await _fileStorage.CopyImageAsync(row.ExplanationImageUrl, "question-images");

                _db.Questions.Add(question);
                createdQuestions.Add(question);
            }

            job.Status = ImportJobStatus.Committed;
            job.ImportedCount = toCommit.Count;
            job.CommittedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            // Auto-mirror every newly-imported PYQ question into the Question Bank (see
            // IQuestionBankMirrorService) -- same reasoning as QuestionsController.Create: a bulk
            // import is just as much "a PYQ upload" as the one-by-one form. Now that bulk-imported
            // questions CAN have images (ZIP upload) and ContentBlocks, MirrorFromPyqAsync/
            // SyncMirrorAsync carry both across -- see that service's own comments on what is and
            // isn't re-copied.
            foreach (var question in createdQuestions)
            {
                var mirrorId = await _mirror.MirrorFromPyqAsync(_db, question, job.Paper.ExamId, job.Paper.Year, adminId);
                if (mirrorId.HasValue) question.MirroredToQuestionBankQuestionId = mirrorId;
            }
            try { await _db.SaveChangesAsync(); } catch { /* non-critical, see MirrorFromPyqAsync's own comment */ }

            // Whatever was staged for this job (whether it made it into a committed question above,
            // or belonged to a row that got skipped/left invalid) has either already been copied
            // elsewhere or is no longer needed -- safe to delete the whole staging folder now. A
            // no-op for a non-ZIP import (nothing was ever staged under this job's id).
            await _fileStorage.DeleteFolderAsync($"bulk-import-staging/{job.Id}");

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
        // created (tagged via Question.ImportJobId), leaving anything entered by hand -- or by a
        // different import job -- untouched. Works regardless of the paper's status (Draft,
        // PendingReview, or Published): the old Draft-only restriction meant a bad import that had
        // already been published couldn't be undone at all, which was the exact pain this whole
        // feature exists to fix. The one real blocker is students: StudentAnswer.QuestionId is a
        // Restrict FK, so a Question a student has already attempted physically cannot be deleted --
        // checked up front so that shows up as a clear message instead of an unhandled SQL error.
        //
        // If this empties the paper completely (every question came from this one job), the paper is
        // sent back to Draft rather than left Published-but-empty or deleted outright -- deleting is a
        // separate, deliberate admin action via PapersController.Delete once they're sure they don't
        // want to reuse it. If the paper's exam was itself created solely for this paper (see
        // Paper.ExamCreatedForThisPaper), the response flags it as an ExamCleanupCandidateId so the
        // frontend can offer a confirm dialog before calling ExamsController.CleanupIfEmpty.
        [HttpPost("bulk-import/{jobId:guid}/rollback")]
        public async Task<ActionResult<BulkImportRollbackResultDto>> Rollback(Guid jobId)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.DeletePaper))
                return Forbid();

            var job = await _db.ImportJobs.Include(j => j.Paper).FirstOrDefaultAsync(j => j.Id == jobId);
            if (job == null) return NotFound();
            if (job.Status != ImportJobStatus.Committed)
                return BadRequest(new { message = "Only a committed import can be rolled back." });
            if (job.Paper == null)
                return BadRequest(new { message = "This import's paper no longer exists." });

            var hasAttempts = await _db.StudentTestResults.AnyAsync(r => r.PaperId == job.PaperId);
            if (hasAttempts)
                return Conflict(new { message = "Students have already attempted this paper -- these questions can't be safely removed anymore. Unpublish the paper and fix the affected questions by hand instead." });

            var wasPublished = job.Paper.Status == PaperStatus.Published;

            var imported = await _db.Questions.Where(q => q.ImportJobId == jobId).ToListAsync();
            var importedIds = imported.Select(q => q.Id).ToList();

            // Now that a ZIP-based bulk import can give a question images, a rollback needs to clean
            // those up too -- same imageUrls-array-then-delete-after-remove pattern as
            // QuestionsController.Delete. A no-op for questions from a CSV/Excel/JSON import, which
            // never had any of these fields set.
            var imageUrls = imported
                .SelectMany(q => new[]
                {
                    q.QuestionImageUrl, q.OptionAImageUrl, q.OptionBImageUrl,
                    q.OptionCImageUrl, q.OptionDImageUrl, q.ExplanationImageUrl
                })
                .ToList();

            // Computed BEFORE the removal below (from what's still in the DB) so this doesn't need a
            // second round trip after SaveChangesAsync -- QuestionBankLinks are never touched by a
            // rollback, so only the PYQ Questions side can shrink.
            var otherQuestionsCount = await _db.Questions.CountAsync(q => q.PaperId == job.PaperId && q.ImportJobId != jobId);
            var qbLinksCount = await _db.PaperQuestionBankLinks.CountAsync(l => l.PaperId == job.PaperId);
            var paperNowEmpty = otherQuestionsCount == 0 && qbLinksCount == 0;

            _db.Questions.RemoveRange(imported);

            job.Status = ImportJobStatus.RolledBack;
            job.RolledBackAt = DateTime.UtcNow;

            if (paperNowEmpty)
            {
                job.Paper.Status = PaperStatus.Draft;
                job.Paper.PublishedAt = null;
            }

            await _db.SaveChangesAsync();
            foreach (var url in imageUrls) await _fileStorage.DeleteImageAsync(url);

            if (wasPublished)
            {
                try { await _instantSearch.RemoveQuestionsAsync(importedIds); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to remove {Count} rolled-back question(s) from the search index", importedIds.Count); }
            }

            await _audit.LogAsync(User.GetAdminId(), "BulkImport.Rollback", "Paper", job.PaperId, $"{imported.Count} question(s) removed from {job.FileName}");

            Guid? examCleanupCandidateId = (paperNowEmpty && job.Paper.ExamCreatedForThisPaper) ? job.Paper.ExamId : null;

            return Ok(new BulkImportRollbackResultDto
            {
                JobId = job.Id,
                QuestionsRemoved = imported.Count,
                PaperStatus = job.Paper.Status.ToString(),
                ExamCleanupCandidateId = examCleanupCandidateId
            });
        }

        private static ImportFileFormat? DetectFormat(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".csv" => ImportFileFormat.Csv,
                ".xlsx" => ImportFileFormat.Excel,
                ".json" => ImportFileFormat.Json,
                ".zip" => ImportFileFormat.Zip,
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
