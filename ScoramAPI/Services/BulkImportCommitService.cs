using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    public interface IBulkImportCommitService
    {
        // Throws InvalidOperationException (with a user-facing message) for validation failures --
        // job not found, already committed/rolled-back/failed, paper no longer Draft, or the preview
        // cache entry expired. Callers translate that into a 400 (BulkImportController's inline
        // fallback path) or a Failed status + logged message (BulkImportCommitWorker).
        Task<BulkImportCommitResultDto> CommitAsync(Guid jobId, Guid adminId, List<int>? rowNumbers);
    }

    // Extracted from BulkImportController.Commit so the exact same logic runs whether the queue is
    // available (BulkImportCommitWorker calls this from its own DI scope, no HttpContext/User to
    // read AdminId from -- see BulkImportCommitJob) or not (the controller calls this directly and
    // returns its result immediately, today's original behavior).
    public class BulkImportCommitService : IBulkImportCommitService
    {
        private const string CachePrefix = "bulk-import-rows:";

        private readonly ScoramDbContext _db;
        private readonly IFileStorageService _fileStorage;
        private readonly IStagedDataCache _cache;
        private readonly IAuditLogService _audit;
        private readonly IQuestionBankMirrorService _mirror;

        public BulkImportCommitService(
            ScoramDbContext db, IFileStorageService fileStorage, IStagedDataCache cache,
            IAuditLogService audit, IQuestionBankMirrorService mirror)
        {
            _db = db;
            _fileStorage = fileStorage;
            _cache = cache;
            _audit = audit;
            _mirror = mirror;
        }

        public async Task<BulkImportCommitResultDto> CommitAsync(Guid jobId, Guid adminId, List<int>? rowNumbers)
        {
            var job = await _db.ImportJobs.Include(j => j.Paper).FirstOrDefaultAsync(j => j.Id == jobId);
            if (job == null) throw new InvalidOperationException("Import job not found.");

            // Processing is included here (not just PendingReview) because by the time this runs via
            // the queued path, BulkImportController.Commit has already moved the job to Processing
            // right before enqueueing it -- see that method's own comment.
            if (job.Status != ImportJobStatus.PendingReview && job.Status != ImportJobStatus.Processing)
                throw new InvalidOperationException($"This import is already {job.Status} and can't be committed again.");
            if (job.Paper == null || job.Paper.Status != PaperStatus.Draft)
                throw new InvalidOperationException("The paper is no longer in Draft -- can't commit into it.");

            var rows = await _cache.GetAsync<List<ImportedQuestionRow>>(CachePrefix + jobId);
            if (rows == null)
                throw new InvalidOperationException("This preview has expired (previews last 30 minutes). Please re-upload the file.");

            var wanted = rowNumbers != null ? new HashSet<int>(rowNumbers) : null;
            var toCommit = rows.Where(r => r.IsValid && (wanted == null || wanted.Contains(r.RowNumber))).ToList();
            var skipped = rows.Count - toCommit.Count;

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

                // See the identical comment in the pre-extraction version of this method (now here)
                // and in QuestionBankAdminController.Commit for why these run concurrently.
                var questionImageTask = _fileStorage.CopyImageAsync(row.QuestionImageUrl, "question-images");
                var optionAImageTask = _fileStorage.CopyImageAsync(row.OptionAImageUrl, "question-images");
                var optionBImageTask = _fileStorage.CopyImageAsync(row.OptionBImageUrl, "question-images");
                var optionCImageTask = _fileStorage.CopyImageAsync(row.OptionCImageUrl, "question-images");
                var optionDImageTask = _fileStorage.CopyImageAsync(row.OptionDImageUrl, "question-images");
                var explanationImageTask = _fileStorage.CopyImageAsync(row.ExplanationImageUrl, "question-images");
                await Task.WhenAll(questionImageTask, optionAImageTask, optionBImageTask, optionCImageTask, optionDImageTask, explanationImageTask);
                question.QuestionImageUrl = questionImageTask.Result;
                question.OptionAImageUrl = optionAImageTask.Result;
                question.OptionBImageUrl = optionBImageTask.Result;
                question.OptionCImageUrl = optionCImageTask.Result;
                question.OptionDImageUrl = optionDImageTask.Result;
                question.ExplanationImageUrl = explanationImageTask.Result;

                _db.Questions.Add(question);
                createdQuestions.Add(question);
            }

            job.Status = ImportJobStatus.Committed;
            job.ImportedCount = toCommit.Count;
            job.CommittedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            foreach (var question in createdQuestions)
            {
                var mirrorId = await _mirror.MirrorFromPyqAsync(_db, question, job.Paper.ExamId, job.Paper.Year, adminId);
                if (mirrorId.HasValue) question.MirroredToQuestionBankQuestionId = mirrorId;
            }
            try { await _db.SaveChangesAsync(); } catch { /* non-critical, see MirrorFromPyqAsync's own comment */ }

            await _fileStorage.DeleteFolderAsync($"bulk-import-staging/{job.Id}");
            await _cache.RemoveAsync(CachePrefix + jobId);
            await _audit.LogAsync(adminId, "BulkImport.Commit", "Paper", job.PaperId, $"{toCommit.Count} question(s) imported from {job.FileName}");

            return new BulkImportCommitResultDto
            {
                JobId = job.Id,
                Status = job.Status.ToString(),
                ImportedCount = toCommit.Count,
                SkippedCount = skipped
            };
        }
    }
}
