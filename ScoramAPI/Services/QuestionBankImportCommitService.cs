using Microsoft.EntityFrameworkCore;
using ScoramAPI.Controllers;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    public interface IQuestionBankImportCommitService
    {
        // Throws InvalidOperationException (user-facing message) for validation failures -- job not
        // found, already committed/rolled-back/failed, preview cache expired, or (rare -- see
        // ResolveExamAndTrackAsync below) an exam a row named was deleted since Preview.
        Task<QuestionBankImportCommitResultDto> CommitAsync(Guid jobId, Guid adminId, List<int>? rowNumbers);
    }

    // Extracted from QuestionBankAdminController.Commit -- see BulkImportCommitService for the
    // sibling this mirrors, and this class's own inline comments (carried over verbatim from the
    // pre-extraction version) for the business logic itself, which is unchanged.
    public class QuestionBankImportCommitService : IQuestionBankImportCommitService
    {
        private const string CachePrefix = "qb-bulk-import-rows:";

        private readonly ScoramDbContext _db;
        private readonly IFileStorageService _fileStorage;
        private readonly IStagedDataCache _cache;
        private readonly IAuditLogService _audit;
        private readonly IQuestionBankImportService _importService;

        public QuestionBankImportCommitService(
            ScoramDbContext db, IFileStorageService fileStorage, IStagedDataCache cache,
            IAuditLogService audit, IQuestionBankImportService importService)
        {
            _db = db;
            _fileStorage = fileStorage;
            _cache = cache;
            _audit = audit;
            _importService = importService;
        }

        public async Task<QuestionBankImportCommitResultDto> CommitAsync(Guid jobId, Guid adminId, List<int>? rowNumbers)
        {
            var job = await _db.QuestionBankImportJobs.FindAsync(jobId);
            if (job == null) throw new InvalidOperationException("Import job not found.");

            // Processing included alongside PendingReview for the same reason as
            // BulkImportCommitService's identical check -- see that method's own comment.
            if (job.Status != ImportJobStatus.PendingReview && job.Status != ImportJobStatus.Processing)
                throw new InvalidOperationException($"This import is already {job.Status} and can't be committed again.");

            var rows = await _cache.GetAsync<List<QuestionBankImportRow>>(CachePrefix + jobId);
            if (rows == null)
                throw new InvalidOperationException("This preview has expired (previews last 30 minutes). Please re-upload the file.");

            var wanted = rowNumbers != null ? new HashSet<int>(rowNumbers) : null;
            var toCommit = rows.Where(r => r.IsValid && (wanted == null || wanted.Contains(r.RowNumber))).ToList();
            var skipped = rows.Count - toCommit.Count;

            var subjectCache = await _db.QuestionBankSubjects.ToDictionaryAsync(s => s.Name, StringComparer.OrdinalIgnoreCase);
            var topicCache = await _db.QuestionBankTopics.ToDictionaryAsync(t => (t.SubjectId, t.Name), new TopicKeyComparer());
            var examCache = await _db.Exams.ToDictionaryAsync(e => e.Name, StringComparer.OrdinalIgnoreCase);

            var importedCount = 0;
            var mergedCount = 0;

            var examsTouchedThisBatch = new HashSet<Guid>();
            var candidateEmptyExamIds = new List<Guid>();

            async Task<Exam> ResolveExamAndTrackAsync(string examName)
            {
                var name = examName.Trim();
                if (!examCache.TryGetValue(name, out var exam))
                {
                    exam = await _db.Exams.FirstOrDefaultAsync(e => e.Name == name)
                        ?? throw new InvalidOperationException(
                            $"Exam \"{name}\" no longer exists -- it may have been deleted since this file was previewed. Re-upload and try again.");
                    examCache[name] = exam;
                }
                if (!examsTouchedThisBatch.Contains(exam.Id))
                {
                    var wasEmpty = !await ExamsController.ExamHasContentAsync(_db, exam.Id, exam.Name);
                    if (wasEmpty) candidateEmptyExamIds.Add(exam.Id);
                    examsTouchedThisBatch.Add(exam.Id);
                }
                return exam;
            }

            foreach (var row in toCommit)
            {
                if (row.IsDuplicate && row.DuplicateOfQuestionId.HasValue)
                {
                    var existingMappings = await _db.QuestionBankExamMappings
                        .Where(m => m.QuestionBankQuestionId == row.DuplicateOfQuestionId.Value)
                        .ToListAsync();

                    foreach (var ey in row.ExamYears)
                    {
                        var exam = await ResolveExamAndTrackAsync(ey.ExamName!);
                        var alreadyMapped = existingMappings.Any(m => m.ExamId == exam.Id && m.Year == ey.Year);
                        if (alreadyMapped) continue;

                        _db.QuestionBankExamMappings.Add(new QuestionBankExamMapping
                        {
                            QuestionBankQuestionId = row.DuplicateOfQuestionId.Value,
                            ExamId = exam.Id,
                            Year = ey.Year,
                            ImportJobId = job.Id
                        });
                    }
                    mergedCount++;
                    continue;
                }

                if (row.IsDuplicate) { mergedCount++; continue; }

                var subject = await GetOrCreateSubjectCachedAsync(row.Subject, adminId, subjectCache);
                var topic = await GetOrCreateTopicCachedAsync(subject.Id, row.Topic, adminId, topicCache);

                var question = new QuestionBankQuestion
                {
                    QuestionText = row.QuestionText.Trim(),
                    NormalizedQuestionText = _importService.NormalizeForDuplicateCheck(row.QuestionText),
                    OptionA = row.OptionA.Trim(),
                    OptionB = row.OptionB.Trim(),
                    OptionC = row.OptionC.Trim(),
                    OptionD = row.OptionD.Trim(),
                    CorrectOption = Enum.Parse<OptionLetter>(row.CorrectOption, ignoreCase: true),
                    Explanation = row.Explanation,
                    ContentBlocksJson = row.ContentBlocksJson,
                    SubjectId = subject.Id,
                    TopicId = topic.Id,
                    SourceReference = row.SourceReference,
                    Language = ParseLanguage(row.Language),
                    CreatedByAdminId = adminId,
                    ImportJobId = job.Id,
                    CreatedAt = DateTime.UtcNow
                };

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

                foreach (var ey in row.ExamYears)
                {
                    var exam = await ResolveExamAndTrackAsync(ey.ExamName!);
                    question.ExamMappings.Add(new QuestionBankExamMapping { Exam = exam, Year = ey.Year });
                }

                _db.QuestionBankQuestions.Add(question);
                importedCount++;
            }

            job.Status = ImportJobStatus.Committed;
            job.ImportedCount = importedCount;
            job.MergedIntoExistingCount = mergedCount;
            job.CommittedAt = DateTime.UtcNow;
            job.CandidateEmptyExamIds = candidateEmptyExamIds.Count > 0
                ? string.Join(",", candidateEmptyExamIds.Distinct())
                : null;

            await _db.SaveChangesAsync();

            await _fileStorage.DeleteFolderAsync($"bulk-import-staging/{job.Id}");
            await _cache.RemoveAsync(CachePrefix + jobId);
            await _audit.LogAsync(adminId, "QuestionBank.BulkImport.Commit", "QuestionBankImportJob", job.Id,
                $"{importedCount} new, {mergedCount} merged, from {job.FileName}");

            return new QuestionBankImportCommitResultDto
            {
                JobId = job.Id,
                Status = job.Status.ToString(),
                ImportedCount = importedCount,
                MergedIntoExistingCount = mergedCount,
                SkippedCount = skipped
            };
        }

        private async Task<QuestionBankSubject> GetOrCreateSubjectCachedAsync(string subjectName, Guid adminId, Dictionary<string, QuestionBankSubject> cache)
        {
            var name = subjectName.Trim();
            if (cache.TryGetValue(name, out var cached)) return cached;

            var subject = new QuestionBankSubject { Name = name, CreatedByAdminId = adminId };
            _db.QuestionBankSubjects.Add(subject);
            await _db.SaveChangesAsync();
            cache[name] = subject;
            return subject;
        }

        private async Task<QuestionBankTopic> GetOrCreateTopicCachedAsync(Guid subjectId, string topicName, Guid adminId, Dictionary<(Guid, string), QuestionBankTopic> cache)
        {
            var name = topicName.Trim();
            var key = (subjectId, name);
            if (cache.TryGetValue(key, out var cached)) return cached;

            var topic = new QuestionBankTopic { SubjectId = subjectId, Name = name, CreatedByAdminId = adminId };
            _db.QuestionBankTopics.Add(topic);
            await _db.SaveChangesAsync();
            cache[key] = topic;
            return topic;
        }

        // Duplicated from QuestionBankAdminController rather than shared -- both are small, private,
        // and stateless; not worth widening either's visibility just to avoid a few lines twice.
        private class TopicKeyComparer : IEqualityComparer<(Guid SubjectId, string Name)>
        {
            public bool Equals((Guid SubjectId, string Name) x, (Guid SubjectId, string Name) y) =>
                x.SubjectId == y.SubjectId && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            public int GetHashCode((Guid SubjectId, string Name) obj) =>
                HashCode.Combine(obj.SubjectId, obj.Name.ToLowerInvariant());
        }

        private static PaperLanguage? ParseLanguage(string? raw) =>
            !string.IsNullOrWhiteSpace(raw) && Enum.TryParse<PaperLanguage>(raw, ignoreCase: true, out var parsed) ? parsed : null;
    }
}
