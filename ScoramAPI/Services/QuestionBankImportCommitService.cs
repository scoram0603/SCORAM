using Microsoft.Data.SqlClient;
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
        private readonly ILogger<QuestionBankImportCommitService> _logger;

        public QuestionBankImportCommitService(
            ScoramDbContext db, IFileStorageService fileStorage, IStagedDataCache cache,
            IAuditLogService audit, IQuestionBankImportService importService,
            ILogger<QuestionBankImportCommitService> logger)
        {
            _logger = logger;
            _db = db;
            _fileStorage = fileStorage;
            _cache = cache;
            _audit = audit;
            _importService = importService;
        }

        public async Task<QuestionBankImportCommitResultDto> CommitAsync(Guid jobId, Guid adminId, List<int>? rowNumbers)
        {
            try
            {
                return await CommitCoreAsync(jobId, adminId, rowNumbers);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // The DB's unique index on (QuestionBankQuestionId, ExamId, Year) is the final guard
                // against a CONCURRENT import that tagged the same question+exam+year between our
                // read and our save. CommitCoreAsync rolled its transaction back; one retry from a
                // clean tracker re-reads the now-existing mappings and simply skips them. If it still
                // fails, the exception propagates -- we never swallow it.
                _logger.LogWarning(ex, "Question-bank import {JobId}: unique-index conflict (concurrent import?), retrying once.", jobId);
                _db.ChangeTracker.Clear();
                return await CommitCoreAsync(jobId, adminId, rowNumbers);
            }
        }

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);

        private async Task<QuestionBankImportCommitResultDto> CommitCoreAsync(Guid jobId, Guid adminId, List<int>? rowNumbers)
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

            // ---- RULE 1: question identity = normalized text + ALL options ----
            // Resolved HERE from the rows themselves (not from the preview's IsDuplicate flags), so
            // the result is correct even if the DB changed since preview, and so duplicates INSIDE
            // this file resolve to the one question this loop creates for the first of them.
            // Loaded once for the whole batch.
            var existingByKey = await _importService.LoadExistingByDuplicateKeyAsync(_db, toCommit);
            var questionIdByKey = existingByKey.ToDictionary(kv => kv.Key, kv => kv.Value.Id);
            var createdThisBatch = new HashSet<string>(); // keys whose question THIS loop created

            // ---- RULE 2: mapping identity = QuestionBankQuestionId + ExamId + Year ----
            // Seeded with every mapping already in the DB for the reused questions (one query per
            // 500 ids, not per row). Every mapping this loop adds goes into the SAME set, so a
            // mapping staged earlier in this batch (not yet saved, invisible to a DB query) is
            // also caught.
            var mappingKeys = new HashSet<(Guid QuestionId, Guid ExamId, int Year)>();
            foreach (var chunk in questionIdByKey.Values.Distinct().Chunk(500))
            {
                var existingMappings = await _db.QuestionBankExamMappings
                    .AsNoTracking()
                    .Where(m => chunk.Contains(m.QuestionBankQuestionId))
                    .Select(m => new { m.QuestionBankQuestionId, m.ExamId, m.Year })
                    .ToListAsync();
                foreach (var m in existingMappings)
                    mappingKeys.Add((m.QuestionBankQuestionId, m.ExamId, m.Year));
            }

            // One transaction around the whole commit: GetOrCreateSubject/Topic below call
            // SaveChangesAsync mid-loop (so the next row can see them), which previously meant a
            // failure left half-committed subjects/topics behind. No retry execution strategy is
            // configured in Program.cs, so a user-initiated transaction is fine; ScoramDbContext's
            // SaveChangesAsync joins it.
            await using var tx = await _db.Database.BeginTransactionAsync();

            foreach (var row in toCommit)
            {
                var key = _importService.BuildDuplicateKey(row.QuestionText, row.OptionA, row.OptionB, row.OptionC, row.OptionD);

                Guid questionId;
                bool addedToExistingQuestion;

                if (questionIdByKey.TryGetValue(key, out var reusedId))
                {
                    // Same text + same options already exists (in DB, or created earlier in this
                    // very file) -> reuse it, never a second QuestionBankQuestion.
                    questionId = reusedId;
                    addedToExistingQuestion = !createdThisBatch.Contains(key);
                    mergedCount++;
                }
                else
                {
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

                    _db.QuestionBankQuestions.Add(question);
                    questionId = question.Id;
                    addedToExistingQuestion = false;
                    questionIdByKey[key] = questionId;
                    createdThisBatch.Add(key);
                    importedCount++;
                }

                foreach (var ey in row.ExamYears)
                {
                    var exam = await ResolveExamAndTrackAsync(ey.ExamName!);

                    // HashSet.Add returns false if (question, exam, year) is already known -- from
                    // the DB, from an earlier row, or from a repeated pair in this same row
                    // ("SSC CGL:2024; ssc cgl:2024"). Skip it: exactly one mapping per combination.
                    if (!mappingKeys.Add((questionId, exam.Id, ey.Year))) continue;

                    _db.QuestionBankExamMappings.Add(new QuestionBankExamMapping
                    {
                        QuestionBankQuestionId = questionId,
                        ExamId = exam.Id,
                        Year = ey.Year,
                        // Tagged only when added onto a question that existed BEFORE this job, so
                        // rollback can remove just those; a question created by this job is deleted
                        // wholesale on rollback and its mappings cascade (see model comment).
                        ImportJobId = addedToExistingQuestion ? job.Id : null
                    });
                }
            }

            job.Status = ImportJobStatus.Committed;
            job.ImportedCount = importedCount;
            job.MergedIntoExistingCount = mergedCount;
            job.CommittedAt = DateTime.UtcNow;
            job.CandidateEmptyExamIds = candidateEmptyExamIds.Count > 0
                ? string.Join(",", candidateEmptyExamIds.Distinct())
                : null;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

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
