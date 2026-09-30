using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    // Thrown for every "expected" failure (validation, duplicate, not found, blocked, stale) so the
    // controller can turn it into a friendly { message, code, data } response with the right HTTP
    // status -- never a raw SQL error or stack trace. Anything NOT wrapped in this still falls
    // through to ExceptionHandlingMiddleware's generic 500.
    public class SubjectOpException : Exception
    {
        public int Status { get; }
        public string Code { get; }
        public object? Payload { get; }

        public SubjectOpException(int status, string code, string message, object? data = null) : base(message)
        {
            Status = status;
            Code = code;
            Payload = data;
        }
    }

    public interface ISubjectManagementService
    {
        Task<SubjectListResponseDto> ListAsync(SubjectListQueryDto query, CancellationToken ct);
        Task<SubjectDetailDto> GetAsync(Guid id, CancellationToken ct);
        Task<SubjectOperationResultDto> CreateAsync(Guid adminId, SubjectCreateRequestDto dto, CancellationToken ct);
        Task<SubjectOperationResultDto> UpdateAsync(Guid adminId, Guid id, SubjectUpdateRequestDto dto, CancellationToken ct);
        Task<SubjectOperationResultDto> SetActiveAsync(Guid adminId, Guid id, SubjectSetActiveRequestDto dto, CancellationToken ct);
        Task<SubjectImpactPreviewDto> PreviewMergeAsync(SubjectMergePreviewRequestDto dto, CancellationToken ct);
        Task<SubjectOperationResultDto> MergeAsync(Guid adminId, SubjectMergeRequestDto dto, CancellationToken ct);
        Task<SubjectImpactPreviewDto> PreviewReassignAsync(SubjectReassignPreviewRequestDto dto, CancellationToken ct);
        Task<SubjectOperationResultDto> ReassignAsync(Guid adminId, SubjectReassignRequestDto dto, CancellationToken ct);
        Task<SubjectDeletePreviewDto> PreviewDeleteAsync(Guid id, CancellationToken ct);
        Task<SubjectOperationResultDto> DeleteAsync(Guid adminId, Guid id, bool confirm, string? confirmName, CancellationToken ct);
    }

    // ==================================================================================
    // How a Subject is linked to content in SCORAM today (read from the code, not assumed):
    //
    //   FK  (SubjectId)   QuestionBankQuestion.SubjectId        -> "PYQ" / Question Bank questions
    //                     QuestionBankTopic.SubjectId           -> Topics (children of a subject)
    //                     PracticeTestTemplate.SubjectId        -> Practice Test templates (nullable)
    //   TEXT (by name)    Question.Subject                      -> legacy "PYP" paper questions (plain string, free-typed)
    //   Derived only      Mock Tests / Quizzes / Papers         -> no SubjectId of their own; they hold questions,
    //                                                              so they follow a question's subject automatically
    //   Snapshots         StudentTestResult.PracticeSubjectId / StudentAnswer.SubjectSnapshot
    //                                                           -> history; deliberately never rewritten
    //
    // So a rename/merge/reassign has to (a) move the FK rows and (b) rewrite the legacy TEXT so the
    // two representations of "the same subject" never drift apart (the auto-mirror that copies a
    // PYP question into the Question Bank resolves its subject BY NAME).
    // ==================================================================================
    public class SubjectManagementService : ISubjectManagementService
    {
        // Question.Subject is MaxLength(50) (QuestionBankSubject.Name allows 100) -- a longer name can't be
        // written back into legacy PYP rows, so it's refused only when there ARE legacy rows to write.
        private const int LegacySubjectMaxLength = 50;
        private const int SubjectNameMaxLength = 100;
        private const int MaxMergeSources = 25;

        private readonly ScoramDbContext _db;
        private readonly IBackgroundJobQueue _jobQueue;
        private readonly IInstantSearchService _instantSearch;
        private readonly ILogger<SubjectManagementService> _logger;

        public SubjectManagementService(
            ScoramDbContext db, IBackgroundJobQueue jobQueue, IInstantSearchService instantSearch,
            ILogger<SubjectManagementService> logger)
        {
            _db = db;
            _jobQueue = jobQueue;
            _instantSearch = instantSearch;
            _logger = logger;
        }

        // ------------------------------------------------------------------------------
        // Normalisation
        // ------------------------------------------------------------------------------

        // Trim + collapse inner runs of whitespace. " Reasoning ", "Reasoning" and "Reasoning  " all become "Reasoning".
        public static string NormalizeName(string? raw) => Regex.Replace((raw ?? string.Empty).Trim(), @"\s+", " ");

        // Case-insensitive comparison key for duplicate detection. Purely textual -- "General Knowledge"
        // and "General Awareness" are different keys, and nothing here ever tries to guess they're related.
        public static string NameKey(string? raw) => NormalizeName(raw).ToLowerInvariant();

        // Key for matching legacy Question.Subject text -- deliberately the exact same shape the SQL side
        // uses (LTRIM(RTRIM(LOWER(x)))) so in-memory and database matching agree.
        private static string LegacyKey(string? raw) => (raw ?? string.Empty).Trim().ToLowerInvariant();

        private static string TopicKey(string? raw) => NormalizeName(raw).ToLowerInvariant();

        private static string VersionOf(QuestionBankSubject s) => (s.UpdatedAt ?? s.CreatedAt).Ticks.ToString();

        // ------------------------------------------------------------------------------
        // Usage counting (real queries only -- nothing hard-coded, nothing fabricated)
        // ------------------------------------------------------------------------------

        // includeDistinct: distinct Mock Tests / Quizzes / Papers per subject (needs a few extra queries).
        // includeAttempts: past Practice attempts pointing at the subject (scans the attempts table -- only
        //                  needed when deciding whether a hard delete is safe).
        private async Task<Dictionary<Guid, SubjectUsageDto>> ComputeUsageAsync(
            IReadOnlyCollection<QuestionBankSubject> subjects, bool includeDistinct, bool includeAttempts, CancellationToken ct)
        {
            var usage = subjects.ToDictionary(s => s.Id, _ => new SubjectUsageDto());
            if (subjects.Count == 0) return usage;

            var ids = subjects.Select(s => s.Id).ToList();

            // Question Bank ("PYQ") questions -- every row, active or not, since a merge moves them all.
            var qb = await _db.QuestionBankQuestions
                .Where(q => ids.Contains(q.SubjectId))
                .GroupBy(q => q.SubjectId)
                .Select(g => new { SubjectId = g.Key, Total = g.Count(), Inactive = g.Count(q => !q.IsActive) })
                .ToListAsync(ct);

            // Question Bank rows that are just the auto-mirror of a legacy PYP question -- subtracted so the
            // de-duplicated "Total Questions" doesn't count the same question twice (same rule as
            // PublicStatsController / ExamsController.GetCombinedQuestionCountAsync).
            var mirrors = await _db.Questions
                .Where(l => l.MirroredToQuestionBankQuestionId != null)
                .Join(_db.QuestionBankQuestions.Where(q => ids.Contains(q.SubjectId)),
                      l => l.MirroredToQuestionBankQuestionId, q => (Guid?)q.Id,
                      (l, q) => new { q.SubjectId, QuestionId = q.Id })
                .GroupBy(x => x.SubjectId)
                .Select(g => new { SubjectId = g.Key, Count = g.Select(x => x.QuestionId).Distinct().Count() })
                .ToListAsync(ct);

            var topics = await _db.QuestionBankTopics
                .Where(t => ids.Contains(t.SubjectId))
                .GroupBy(t => t.SubjectId)
                .Select(g => new { SubjectId = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var templates = await _db.PracticeTestTemplates
                .Where(t => t.SubjectId != null && ids.Contains(t.SubjectId.Value))
                .GroupBy(t => t.SubjectId!.Value)
                .Select(g => new { SubjectId = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            // Legacy PYP questions: grouped by the raw stored text, then folded onto subjects in memory by
            // normalised name (keeps the DB work to one index-friendly GROUP BY).
            var legacyRaw = await _db.Questions
                .GroupBy(q => q.Subject)
                .Select(g => new { Subject = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            var legacyByKey = legacyRaw.GroupBy(x => LegacyKey(x.Subject)).ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

            // Mock Test / Quiz question slots (a slot = one question placed in one test).
            var mockQbSlots = await _db.MockTestQuestions
                .Where(m => m.QuestionBankQuestionId != null)
                .Join(_db.QuestionBankQuestions.Where(q => ids.Contains(q.SubjectId)),
                      m => m.QuestionBankQuestionId, q => (Guid?)q.Id, (m, q) => new { q.SubjectId })
                .GroupBy(x => x.SubjectId)
                .Select(g => new { SubjectId = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var mockLegacyRaw = await _db.MockTestQuestions
                .Where(m => m.QuestionId != null)
                .GroupBy(m => m.Question!.Subject)
                .Select(g => new { Subject = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            var mockLegacyByKey = mockLegacyRaw.GroupBy(x => LegacyKey(x.Subject)).ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

            var quizSlots = await _db.QuizQuestions
                .Join(_db.QuestionBankQuestions.Where(q => ids.Contains(q.SubjectId)),
                      z => z.QuestionBankQuestionId, q => q.Id, (z, q) => new { q.SubjectId })
                .GroupBy(x => x.SubjectId)
                .Select(g => new { SubjectId = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var mirrorBySubject = mirrors.ToDictionary(x => x.SubjectId, x => x.Count);

            foreach (var s in subjects)
            {
                var u = usage[s.Id];
                var qbRow = qb.FirstOrDefault(x => x.SubjectId == s.Id);
                var key = LegacyKey(s.Name);

                u.QuestionBankQuestions = qbRow?.Total ?? 0;
                u.InactiveQuestionBankQuestions = qbRow?.Inactive ?? 0;
                u.PypQuestions = legacyByKey.GetValueOrDefault(key);
                u.Topics = topics.FirstOrDefault(x => x.SubjectId == s.Id)?.Count ?? 0;
                u.PracticeTemplates = templates.FirstOrDefault(x => x.SubjectId == s.Id)?.Count ?? 0;
                u.TotalAffected = u.QuestionBankQuestions + u.PypQuestions + u.Topics + u.PracticeTemplates;

                u.TotalQuestions = Math.Max(0, u.QuestionBankQuestions - mirrorBySubject.GetValueOrDefault(s.Id)) + u.PypQuestions;
                u.MockTestQuestions = (mockQbSlots.FirstOrDefault(x => x.SubjectId == s.Id)?.Count ?? 0) + mockLegacyByKey.GetValueOrDefault(key);
                u.QuizQuestions = quizSlots.FirstOrDefault(x => x.SubjectId == s.Id)?.Count ?? 0;
            }

            if (includeDistinct)
            {
                // Distinct (test, subject) pairs -- small (tests x subjects), so brought into memory and
                // folded together there; that's what lets a test holding BOTH a Question Bank and a legacy
                // question of the same subject count once instead of twice.
                var mockQbPairs = await _db.MockTestQuestions
                    .Where(m => m.QuestionBankQuestionId != null)
                    .Join(_db.QuestionBankQuestions.Where(q => ids.Contains(q.SubjectId)),
                          m => m.QuestionBankQuestionId, q => (Guid?)q.Id, (m, q) => new { q.SubjectId, m.MockTestId })
                    .Distinct().ToListAsync(ct);
                var mockLegacyPairs = await _db.MockTestQuestions
                    .Where(m => m.QuestionId != null)
                    .Select(m => new { m.MockTestId, Subject = m.Question!.Subject })
                    .Distinct().ToListAsync(ct);

                var quizPairs = await _db.QuizQuestions
                    .Join(_db.QuestionBankQuestions.Where(q => ids.Contains(q.SubjectId)),
                          z => z.QuestionBankQuestionId, q => q.Id, (z, q) => new { q.SubjectId, z.QuizId })
                    .Distinct().ToListAsync(ct);

                var paperQbPairs = await _db.PaperQuestionBankLinks
                    .Join(_db.QuestionBankQuestions.Where(q => ids.Contains(q.SubjectId)),
                          l => l.QuestionBankQuestionId, q => q.Id, (l, q) => new { q.SubjectId, l.PaperId })
                    .Distinct().ToListAsync(ct);
                var paperLegacyPairs = await _db.Questions
                    .Where(q => q.PaperId != null)
                    .Select(q => new { PaperId = q.PaperId!.Value, Subject = q.Subject })
                    .Distinct().ToListAsync(ct);

                foreach (var s in subjects)
                {
                    var u = usage[s.Id];
                    var key = LegacyKey(s.Name);

                    u.MockTests = mockQbPairs.Where(x => x.SubjectId == s.Id).Select(x => x.MockTestId)
                        .Union(mockLegacyPairs.Where(x => LegacyKey(x.Subject) == key).Select(x => x.MockTestId)).Distinct().Count();
                    u.Quizzes = quizPairs.Where(x => x.SubjectId == s.Id).Select(x => x.QuizId).Distinct().Count();
                    u.Papers = paperQbPairs.Where(x => x.SubjectId == s.Id).Select(x => x.PaperId)
                        .Union(paperLegacyPairs.Where(x => LegacyKey(x.Subject) == key).Select(x => x.PaperId)).Distinct().Count();
                }
            }

            if (includeAttempts)
            {
                var attempts = await _db.StudentTestResults
                    .Where(r => r.PracticeSubjectId != null && ids.Contains(r.PracticeSubjectId.Value))
                    .GroupBy(r => r.PracticeSubjectId!.Value)
                    .Select(g => new { SubjectId = g.Key, Count = g.Count() })
                    .ToListAsync(ct);
                foreach (var a in attempts) usage[a.SubjectId].PracticeAttempts = a.Count;
            }

            return usage;
        }

        private static SubjectListItemDto ToItem(QuestionBankSubject s, SubjectUsageDto usage) => new()
        {
            Id = s.Id,
            Name = s.Name,
            IsActive = s.IsActive,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt,
            Version = VersionOf(s),
            Usage = usage
        };

        // ------------------------------------------------------------------------------
        // Read
        // ------------------------------------------------------------------------------

        public async Task<SubjectListResponseDto> ListAsync(SubjectListQueryDto query, CancellationToken ct)
        {
            // The subject table is master data (dozens, maybe low hundreds of rows) -- loading it whole and
            // filtering/sorting/paging in memory is what lets us sort by a computed usage column too.
            var all = await _db.QuestionBankSubjects.AsNoTracking().ToListAsync(ct);
            var activeCount = all.Count(s => s.IsActive);
            var inactiveCount = all.Count - activeCount;

            IEnumerable<QuestionBankSubject> filtered = all;
            var status = (query.Status ?? "all").Trim().ToLowerInvariant();
            if (status == "active") filtered = filtered.Where(s => s.IsActive);
            else if (status == "inactive") filtered = filtered.Where(s => !s.IsActive);

            var search = NameKey(query.Search);
            if (search.Length > 0) filtered = filtered.Where(s => NameKey(s.Name).Contains(search));

            var filteredList = filtered.ToList();
            var usage = await ComputeUsageAsync(filteredList, includeDistinct: false, includeAttempts: false, ct);

            var desc = string.Equals(query.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
            Func<QuestionBankSubject, object> keySelector = (query.SortBy ?? "name").Trim().ToLowerInvariant() switch
            {
                "status" => s => s.IsActive ? 0 : 1,
                "questions" => s => usage[s.Id].TotalQuestions,
                "pyp" => s => usage[s.Id].PypQuestions,
                "pyq" => s => usage[s.Id].QuestionBankQuestions,
                "mock" => s => usage[s.Id].MockTestQuestions,
                "quiz" => s => usage[s.Id].QuizQuestions,
                "created" => s => s.CreatedAt,
                "updated" => s => s.UpdatedAt ?? s.CreatedAt,
                _ => s => s.Name.ToLowerInvariant()
            };

            var ordered = desc
                ? filteredList.OrderByDescending(keySelector).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                : filteredList.OrderBy(keySelector).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase);

            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);

            return new SubjectListResponseDto
            {
                Items = ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(s => ToItem(s, usage[s.Id])).ToList(),
                Total = filteredList.Count,
                Page = page,
                PageSize = pageSize,
                ActiveCount = activeCount,
                InactiveCount = inactiveCount
            };
        }

        public async Task<SubjectDetailDto> GetAsync(Guid id, CancellationToken ct)
        {
            var subject = await _db.QuestionBankSubjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
                ?? throw NotFound();

            var usage = (await ComputeUsageAsync(new[] { subject }, includeDistinct: true, includeAttempts: true, ct))[id];

            var topicRows = await _db.QuestionBankTopics.AsNoTracking()
                .Where(t => t.SubjectId == id)
                .OrderBy(t => t.Name)
                .Select(t => new SubjectTopicUsageDto { Id = t.Id, Name = t.Name, IsActive = t.IsActive, QuestionCount = t.Questions.Count })
                .ToListAsync(ct);

            var item = ToItem(subject, usage);
            return new SubjectDetailDto
            {
                Id = item.Id, Name = item.Name, IsActive = item.IsActive, CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt, Version = item.Version, Usage = item.Usage, TopicList = topicRows
            };
        }

        // ------------------------------------------------------------------------------
        // Create
        // ------------------------------------------------------------------------------

        public async Task<SubjectOperationResultDto> CreateAsync(Guid adminId, SubjectCreateRequestDto dto, CancellationToken ct)
        {
            var name = ValidateName(dto.Name);
            await EnsureNameAvailableAsync(name, excludeId: null, ct);

            var subject = new QuestionBankSubject { Name = name, IsActive = dto.IsActive, CreatedByAdminId = adminId };
            _db.QuestionBankSubjects.Add(subject);
            Audit(adminId, "Subject.Create", subject.Id, $"Created subject '{name}' ({(dto.IsActive ? "active" : "inactive")}).");

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Lost a race with another admin creating the same name between the check and the insert.
                throw new SubjectOpException(409, "DUPLICATE_SUBJECT", $"A subject named '{name}' already exists.");
            }

            return new SubjectOperationResultDto
            {
                Message = $"Subject '{name}' created.",
                Subject = ToItem(subject, new SubjectUsageDto())
            };
        }

        // ------------------------------------------------------------------------------
        // Rename
        // ------------------------------------------------------------------------------

        public async Task<SubjectOperationResultDto> UpdateAsync(Guid adminId, Guid id, SubjectUpdateRequestDto dto, CancellationToken ct)
        {
            var newName = ValidateName(dto.Name);

            var existing = await _db.QuestionBankSubjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
                ?? throw NotFound();
            EnsureFresh(existing, dto.Version);

            if (string.Equals(existing.Name, newName, StringComparison.Ordinal))
                throw new SubjectOpException(400, "NO_CHANGE", "The subject name hasn't changed.");

            await EnsureNameAvailableAsync(newName, excludeId: id, ct);

            var usage = (await ComputeUsageAsync(new[] { existing }, includeDistinct: false, includeAttempts: false, ct))[id];
            if (usage.PypQuestions > 0 && newName.Length > LegacySubjectMaxLength)
                throw new SubjectOpException(400, "NAME_TOO_LONG_FOR_PYP",
                    $"This subject is used by {usage.PypQuestions:N0} PYP paper question(s), which can only store subject names up to {LegacySubjectMaxLength} characters. Please use a shorter name.");

            if (usage.TotalAffected > 0 && !dto.Confirm)
                throw new SubjectOpException(409, "CONFIRMATION_REQUIRED",
                    $"This subject is currently used by {usage.TotalAffected:N0} records. Renaming it will update the subject name shown across SCORAM.",
                    new { usage });

            var oldName = existing.Name;
            var paperIds = new List<Guid>();
            var legacyUpdated = 0;

            await RunGuardedAsync(() => InTransactionAsync(async () =>
            {
                var subject = await _db.QuestionBankSubjects.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw NotFound();
                EnsureFresh(subject, dto.Version);
                // Re-check inside the transaction: another admin may have taken the name meanwhile.
                await EnsureNameAvailableAsync(newName, excludeId: id, ct);

                var oldKey = LegacyKey(subject.Name);
                paperIds = await PublishedPaperIdsForSubjectTextAsync(new List<string> { oldKey }, ct);

                subject.Name = newName;
                subject.UpdatedAt = DateTime.UtcNow;

                // Keep legacy PYP text pointing at the SAME subject: the FK rows follow automatically (they
                // reference Id), but the paper questions reference the NAME.
                legacyUpdated = await _db.Questions
                    .Where(q => q.Subject.Trim().ToLower() == oldKey)
                    .ExecuteUpdateAsync(s => s.SetProperty(q => q.Subject, newName), ct);

                Audit(adminId, "Subject.Update", id,
                    $"Renamed '{oldName}' to '{newName}'. Linked content kept on the same subject record ({usage.QuestionBankQuestions:N0} PYQ, {legacyUpdated:N0} PYP paper question(s) re-labelled).");
                await _db.SaveChangesAsync(ct);
                return true;
            }, ct));

            await ReindexPapersAsync(paperIds);

            var refreshed = await _db.QuestionBankSubjects.AsNoTracking().FirstAsync(s => s.Id == id, ct);
            return new SubjectOperationResultDto
            {
                Message = $"Renamed '{oldName}' to '{newName}'.",
                RecordsUpdated = legacyUpdated,
                Subject = ToItem(refreshed, usage)
            };
        }

        // ------------------------------------------------------------------------------
        // Activate / deactivate ("restore" is simply re-activating)
        // ------------------------------------------------------------------------------

        public async Task<SubjectOperationResultDto> SetActiveAsync(Guid adminId, Guid id, SubjectSetActiveRequestDto dto, CancellationToken ct)
        {
            var subject = await _db.QuestionBankSubjects.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw NotFound();
            EnsureFresh(subject, dto.Version);

            if (subject.IsActive == dto.IsActive)
                throw new SubjectOpException(409, dto.IsActive ? "ALREADY_ACTIVE" : "ALREADY_INACTIVE",
                    dto.IsActive ? $"'{subject.Name}' is already active." : $"'{subject.Name}' is already inactive.");

            subject.IsActive = dto.IsActive;
            subject.UpdatedAt = DateTime.UtcNow;
            Audit(adminId, dto.IsActive ? "Subject.Activate" : "Subject.Deactivate", id,
                dto.IsActive
                    ? $"Activated (restored) subject '{subject.Name}'."
                    : $"Deactivated (archived) subject '{subject.Name}'. Existing content keeps the subject; it no longer appears in new-content dropdowns or student filters.");
            await _db.SaveChangesAsync(ct);

            var usage = (await ComputeUsageAsync(new[] { subject }, includeDistinct: false, includeAttempts: false, ct))[id];
            return new SubjectOperationResultDto
            {
                Message = dto.IsActive ? $"'{subject.Name}' is active again." : $"'{subject.Name}' deactivated. Existing content is untouched.",
                Subject = ToItem(subject, usage)
            };
        }

        // ------------------------------------------------------------------------------
        // Merge / reassign -- preview
        // ------------------------------------------------------------------------------

        public Task<SubjectImpactPreviewDto> PreviewMergeAsync(SubjectMergePreviewRequestDto dto, CancellationToken ct) =>
            BuildImpactAsync("merge", dto.SourceIds, dto.TargetId, ct);

        public Task<SubjectImpactPreviewDto> PreviewReassignAsync(SubjectReassignPreviewRequestDto dto, CancellationToken ct) =>
            BuildImpactAsync("reassign", new List<Guid> { dto.SourceId }, dto.TargetId, ct);

        private async Task<SubjectImpactPreviewDto> BuildImpactAsync(string operation, List<Guid>? rawSourceIds, Guid targetId, CancellationToken ct)
        {
            var (sources, target) = await LoadAndValidateAsync(operation, rawSourceIds, targetId, ct);

            var everyone = sources.Append(target).ToList();
            var usage = await ComputeUsageAsync(everyone, includeDistinct: true, includeAttempts: false, ct);

            var preview = new SubjectImpactPreviewDto
            {
                Operation = operation,
                Sources = sources.Select(s => new SubjectImpactItemDto { Id = s.Id, Name = s.Name, IsActive = s.IsActive, Usage = usage[s.Id] }).ToList(),
                Target = new SubjectImpactItemDto { Id = target.Id, Name = target.Name, IsActive = target.IsActive, Usage = usage[target.Id] }
            };

            // Combined: exact sums for everything that is REWRITTEN (rows are disjoint per source, so no
            // double counting). Distinct Mock/Quiz/Paper counts are left at 0 here on purpose -- one test can
            // hold questions from two sources, so a plain sum would overstate it; they're shown per source.
            var combined = new SubjectUsageDto();
            foreach (var s in sources)
            {
                var u = usage[s.Id];
                combined.QuestionBankQuestions += u.QuestionBankQuestions;
                combined.InactiveQuestionBankQuestions += u.InactiveQuestionBankQuestions;
                combined.PypQuestions += u.PypQuestions;
                combined.Topics += u.Topics;
                combined.PracticeTemplates += u.PracticeTemplates;
                combined.TotalQuestions += u.TotalQuestions;
                combined.MockTestQuestions += u.MockTestQuestions;
                combined.QuizQuestions += u.QuizQuestions;
            }
            combined.TotalAffected = combined.QuestionBankQuestions + combined.PypQuestions + combined.Topics + combined.PracticeTemplates;
            preview.Combined = combined;

            // Topic plan (simulated on names only -- see MoveContentAsync for the real thing).
            var sourceIdList = sources.Select(x => x.Id).ToList();
            var srcTopicNames = await _db.QuestionBankTopics.AsNoTracking()
                .Where(t => sourceIdList.Contains(t.SubjectId))
                .OrderBy(t => t.Name).ThenBy(t => t.Id)
                .Select(t => t.Name).ToListAsync(ct);
            var tgtTopicNames = await _db.QuestionBankTopics.AsNoTracking()
                .Where(t => t.SubjectId == target.Id).Select(t => t.Name).ToListAsync(ct);
            var seen = new HashSet<string>(tgtTopicNames.Select(TopicKey));
            foreach (var n in srcTopicNames)
            {
                if (seen.Add(TopicKey(n))) preview.TopicsToMove++;
                else preview.TopicsToCombine++;
            }

            // Blockers -- anything that means "don't run this right now".
            if (!target.IsActive)
                preview.Blockers.Add($"The target subject '{target.Name}' is inactive. Activate it first -- content moved into an inactive subject would vanish from student filters.");
            if (combined.PypQuestions > 0 && target.Name.Length > LegacySubjectMaxLength)
                preview.Blockers.Add($"'{target.Name}' is longer than {LegacySubjectMaxLength} characters, which is the most a PYP paper question can store as its subject ({combined.PypQuestions:N0} would need it).");

            // Warnings -- things worth reading, but not stopping for.
            if (combined.TotalAffected == 0)
                preview.Warnings.Add("None of the selected source subjects has any linked content -- there is nothing to move.");
            if (preview.TopicsToCombine > 0)
                preview.Warnings.Add($"{preview.TopicsToCombine:N0} topic(s) share a name with a topic already under '{target.Name}'. Their questions will be folded into that existing topic instead of creating a duplicate.");
            if (combined.PypQuestions > 0 && combined.QuestionBankQuestions > 0)
                preview.Warnings.Add("PYP paper questions are also mirrored into the Question Bank, so the same question can appear in both the PYP and PYQ counts.");
            if (operation == "merge")
                preview.Warnings.Add("Source subjects are deactivated (not deleted) after the merge, so nothing is lost and they can be restored.");
            preview.Warnings.Add("Mock Tests, Quizzes and Papers keep their questions; they simply follow each question's new subject. Past student answers keep the subject name they were recorded with.");

            return preview;
        }

        // ------------------------------------------------------------------------------
        // Merge / reassign -- execute
        // ------------------------------------------------------------------------------

        public Task<SubjectOperationResultDto> MergeAsync(Guid adminId, SubjectMergeRequestDto dto, CancellationToken ct) =>
            ExecuteMoveAsync(adminId, "merge", dto.SourceIds, dto.TargetId, dto.Confirm, dto.ConfirmName, dto.ExpectedTotalAffected, ct);

        public Task<SubjectOperationResultDto> ReassignAsync(Guid adminId, SubjectReassignRequestDto dto, CancellationToken ct) =>
            ExecuteMoveAsync(adminId, "reassign", new List<Guid> { dto.SourceId }, dto.TargetId, dto.Confirm, null, dto.ExpectedTotalAffected, ct);

        private async Task<SubjectOperationResultDto> ExecuteMoveAsync(
            Guid adminId, string operation, List<Guid>? rawSourceIds, Guid targetId,
            bool confirm, string? confirmName, int? expectedTotal, CancellationToken ct)
        {
            if (!confirm)
                throw new SubjectOpException(400, "CONFIRMATION_REQUIRED", "Please confirm this action before continuing.");

            var moved = new MoveOutcome();
            var paperIds = new List<Guid>();
            string targetName = string.Empty;

            await RunGuardedAsync(() => InTransactionAsync(async () =>
            {
                // Everything below re-validates against the LIVE database inside the transaction: the admin's
                // preview may be minutes old.
                var (sources, target) = await LoadAndValidateAsync(operation, rawSourceIds, targetId, ct, tracked: true);
                targetName = target.Name;

                if (operation == "merge" && NameKey(confirmName) != NameKey(target.Name))
                    throw new SubjectOpException(400, "CONFIRM_NAME_MISMATCH",
                        $"To confirm, type the target subject's name exactly: {target.Name}");

                if (!target.IsActive)
                    throw new SubjectOpException(400, "TARGET_INACTIVE", $"The target subject '{target.Name}' is inactive. Activate it first.");

                var sourceIds = sources.Select(s => s.Id).ToList();
                var sourceKeys = sources.Select(s => LegacyKey(s.Name)).Distinct().ToList();
                var liveUsage = await ComputeUsageAsync(sources, includeDistinct: false, includeAttempts: false, ct);
                var liveTotal = liveUsage.Values.Sum(u => u.TotalAffected);

                if (liveTotal > 0 && liveUsage.Values.Sum(u => u.PypQuestions) > 0 && target.Name.Length > LegacySubjectMaxLength)
                    throw new SubjectOpException(400, "NAME_TOO_LONG_FOR_PYP",
                        $"'{target.Name}' is longer than {LegacySubjectMaxLength} characters, which PYP paper questions can't store as a subject.");

                if (expectedTotal.HasValue && expectedTotal.Value != liveTotal)
                    throw new SubjectOpException(409, "IMPACT_CHANGED",
                        $"The affected content changed since you opened the preview (it was {expectedTotal.Value:N0}, it is now {liveTotal:N0}). Nothing was changed -- please review the updated preview and confirm again.",
                        new { expected = expectedTotal.Value, actual = liveTotal });

                paperIds = await PublishedPaperIdsForSubjectTextAsync(sourceKeys, ct);
                moved = await MoveContentAsync(sources, target, sourceKeys, ct);

                var now = DateTime.UtcNow;
                target.UpdatedAt = now;
                foreach (var s in sources)
                {
                    s.UpdatedAt = now;
                    if (operation == "merge") s.IsActive = false; // archived, never hard-deleted
                }

                var names = string.Join(", ", sources.Select(s => $"'{s.Name}'"));
                var breakdown = $"{moved.QuestionBank:N0} PYQ, {moved.Pyp:N0} PYP, {moved.TopicsMoved + moved.TopicsCombined:N0} topics ({moved.TopicsCombined:N0} combined), {moved.Templates:N0} practice templates";

                if (operation == "merge")
                {
                    Audit(adminId, "Subject.Merge", target.Id,
                        $"Merged {names} into '{target.Name}'. Moved {breakdown}. Total {moved.Total:N0}. Sources: {string.Join(", ", sourceIds)}.");
                    foreach (var s in sources)
                        Audit(adminId, "Subject.Deactivate", s.Id, $"Deactivated after merge into '{target.Name}' ({target.Id}).");
                }
                else
                {
                    Audit(adminId, "Subject.Reassign", target.Id,
                        $"Reassigned content from {names} to '{target.Name}'. Moved {breakdown}. Total {moved.Total:N0}. Source: {sourceIds[0]}.");
                }

                await _db.SaveChangesAsync(ct);
                return true;
            }, ct));

            await ReindexPapersAsync(paperIds);

            var refreshedTarget = await _db.QuestionBankSubjects.AsNoTracking().FirstAsync(s => s.Id == targetId, ct);
            var targetUsage = (await ComputeUsageAsync(new[] { refreshedTarget }, includeDistinct: false, includeAttempts: false, ct))[targetId];

            return new SubjectOperationResultDto
            {
                Message = operation == "merge"
                    ? $"Merged into '{targetName}'. {moved.Total:N0} records were reassigned; the source subject(s) were deactivated."
                    : $"Reassigned {moved.Total:N0} records to '{targetName}'.",
                RecordsUpdated = moved.Total,
                TopicsMoved = moved.TopicsMoved,
                TopicsCombined = moved.TopicsCombined,
                Subject = ToItem(refreshedTarget, targetUsage)
            };
        }

        private sealed class MoveOutcome
        {
            public int QuestionBank { get; set; }
            public int Pyp { get; set; }
            public int Templates { get; set; }
            public int TopicsMoved { get; set; }
            public int TopicsCombined { get; set; }
            public int Total => QuestionBank + Pyp + Templates + TopicsMoved + TopicsCombined;
        }

        // Runs INSIDE the caller's transaction. Only ever re-points references -- no question, paper, test
        // or quiz row is created, copied or deleted, and no unrelated column (question text, UpdatedAt, ...)
        // is touched.
        private async Task<MoveOutcome> MoveContentAsync(
            List<QuestionBankSubject> sources, QuestionBankSubject target, List<string> sourceLegacyKeys, CancellationToken ct)
        {
            var outcome = new MoveOutcome();
            var sourceIds = sources.Select(s => s.Id).ToList();

            // 1) Topics. A source topic whose name already exists under the target is folded into that
            //    topic (its questions/templates re-pointed); otherwise it just moves under the target.
            var srcTopics = await _db.QuestionBankTopics
                .Where(t => sourceIds.Contains(t.SubjectId))
                .OrderBy(t => t.Name).ThenBy(t => t.Id).ToListAsync(ct);
            var tgtTopics = await _db.QuestionBankTopics.Where(t => t.SubjectId == target.Id).ToListAsync(ct);

            var byKey = new Dictionary<string, QuestionBankTopic>();
            foreach (var t in tgtTopics) byKey.TryAdd(TopicKey(t.Name), t);

            foreach (var topic in srcTopics)
            {
                if (byKey.TryGetValue(TopicKey(topic.Name), out var dest))
                {
                    var topicId = topic.Id;
                    var destId = dest.Id;
                    await _db.QuestionBankQuestions.Where(q => q.TopicId == topicId)
                        .ExecuteUpdateAsync(s => s.SetProperty(q => q.TopicId, destId), ct);
                    await _db.PracticeTestTemplates.Where(t => t.TopicId == topicId)
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.TopicId, (Guid?)destId), ct);

                    // Content is now under the surviving topic -- make sure it isn't hidden by that topic being retired.
                    if (topic.IsActive && !dest.IsActive) dest.IsActive = true;
                    topic.IsActive = false; // the emptied duplicate is retired, not deleted
                    outcome.TopicsCombined++;
                }
                else
                {
                    topic.SubjectId = target.Id;
                    byKey[TopicKey(topic.Name)] = topic;
                    outcome.TopicsMoved++;
                }
            }

            // 2) Question Bank ("PYQ") questions -- every row, active or not.
            var targetId = target.Id;
            outcome.QuestionBank = await _db.QuestionBankQuestions
                .Where(q => sourceIds.Contains(q.SubjectId))
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.SubjectId, targetId), ct);

            // 3) Practice Test templates that filter on the source subject.
            outcome.Templates = await _db.PracticeTestTemplates
                .Where(t => t.SubjectId != null && sourceIds.Contains(t.SubjectId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.SubjectId, (Guid?)targetId), ct);

            // 4) Legacy PYP paper questions -- linked by name, so re-label them to the target's name.
            var targetKey = LegacyKey(target.Name);
            var keysToRewrite = sourceLegacyKeys.Where(k => k != targetKey).ToList();
            if (keysToRewrite.Count > 0)
            {
                var targetName = target.Name;
                outcome.Pyp = await _db.Questions
                    .Where(q => keysToRewrite.Contains(q.Subject.Trim().ToLower()))
                    .ExecuteUpdateAsync(s => s.SetProperty(q => q.Subject, targetName), ct);
            }

            return outcome;
        }

        // ------------------------------------------------------------------------------
        // Delete
        // ------------------------------------------------------------------------------

        public async Task<SubjectDeletePreviewDto> PreviewDeleteAsync(Guid id, CancellationToken ct)
        {
            var subject = await _db.QuestionBankSubjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw NotFound();
            var usage = (await ComputeUsageAsync(new[] { subject }, includeDistinct: true, includeAttempts: true, ct))[id];
            var blockers = DeleteBlockers(usage);
            return new SubjectDeletePreviewDto { Subject = ToItem(subject, usage), CanDelete = blockers.Count == 0, Blockers = blockers };
        }

        private static List<string> DeleteBlockers(SubjectUsageDto u)
        {
            var b = new List<string>();
            if (u.QuestionBankQuestions > 0) b.Add($"{u.QuestionBankQuestions:N0} Question Bank (PYQ) question(s)");
            if (u.PypQuestions > 0) b.Add($"{u.PypQuestions:N0} PYP paper question(s)");
            if (u.Topics > 0) b.Add($"{u.Topics:N0} topic(s)");
            if (u.PracticeTemplates > 0) b.Add($"{u.PracticeTemplates:N0} practice test template(s)");
            if (u.PracticeAttempts > 0) b.Add($"{u.PracticeAttempts:N0} past practice attempt(s) that recorded this subject");
            return b;
        }

        public async Task<SubjectOperationResultDto> DeleteAsync(Guid adminId, Guid id, bool confirm, string? confirmName, CancellationToken ct)
        {
            if (!confirm)
                throw new SubjectOpException(400, "CONFIRMATION_REQUIRED", "Please confirm this action before continuing.");

            var deletedName = string.Empty;

            await RunGuardedAsync(() => InTransactionAsync(async () =>
            {
                var subject = await _db.QuestionBankSubjects.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw NotFound();
                deletedName = subject.Name;

                if (NameKey(confirmName) != NameKey(subject.Name))
                    throw new SubjectOpException(400, "CONFIRM_NAME_MISMATCH", $"To confirm, type the subject's name exactly: {subject.Name}");

                // Re-checked live inside the transaction -- never trust an earlier preview for a delete.
                var usage = (await ComputeUsageAsync(new[] { subject }, includeDistinct: false, includeAttempts: true, ct))[id];
                var blockers = DeleteBlockers(usage);
                if (blockers.Count > 0)
                    throw new SubjectOpException(409, "SUBJECT_IN_USE",
                        "Cannot delete this subject because it is currently used by existing content.",
                        new { usage, blockers });

                _db.QuestionBankSubjects.Remove(subject);
                Audit(adminId, "Subject.Delete", id, $"Permanently deleted subject '{subject.Name}' (it had no linked content).");
                await _db.SaveChangesAsync(ct);
                return true;
            }, ct));

            return new SubjectOperationResultDto { Message = $"Subject '{deletedName}' deleted." };
        }

        // ------------------------------------------------------------------------------
        // Shared helpers
        // ------------------------------------------------------------------------------

        private static SubjectOpException NotFound() =>
            new(404, "SUBJECT_NOT_FOUND", "That subject no longer exists. It may have been deleted -- please refresh the list.");

        private static string ValidateName(string? raw)
        {
            var name = NormalizeName(raw);
            if (name.Length == 0)
                throw new SubjectOpException(400, "NAME_REQUIRED", "Subject name is required.");
            if (name.Length > SubjectNameMaxLength)
                throw new SubjectOpException(400, "NAME_TOO_LONG", $"Subject name can be at most {SubjectNameMaxLength} characters.");
            return name;
        }

        // Duplicate check is case-insensitive over ALL subjects, inactive/merged ones included -- the
        // database's own unique index on Name doesn't distinguish them either, and a retired subject
        // must be restored rather than silently recreated.
        private async Task EnsureNameAvailableAsync(string name, Guid? excludeId, CancellationToken ct)
        {
            var key = NameKey(name);
            var names = await _db.QuestionBankSubjects.AsNoTracking()
                .Select(s => new { s.Id, s.Name, s.IsActive }).ToListAsync(ct);
            var clash = names.FirstOrDefault(s => s.Id != excludeId && NameKey(s.Name) == key);
            if (clash == null) return;

            throw clash.IsActive
                ? new SubjectOpException(409, "DUPLICATE_SUBJECT",
                    $"A subject named '{clash.Name}' already exists. Subject names are compared ignoring capitalisation and extra spaces.",
                    new { existingId = clash.Id })
                : new SubjectOpException(409, "DUPLICATE_INACTIVE_SUBJECT",
                    $"A subject named '{clash.Name}' already exists but is inactive. Restore it from the list instead of creating a new one.",
                    new { existingId = clash.Id });
        }

        private static void EnsureFresh(QuestionBankSubject subject, string? version)
        {
            if (!string.IsNullOrEmpty(version) && version != VersionOf(subject))
                throw new SubjectOpException(409, "CONCURRENT_MODIFICATION",
                    $"'{subject.Name}' was changed by someone else since you opened it. Nothing was saved -- please reload and try again.");
        }

        // Loads + validates the source(s)/target for merge or reassign. Structural problems (missing,
        // duplicated, source == target) throw; state problems (e.g. inactive target) are left to the caller
        // to report as blockers/errors so the preview can still show the numbers.
        private async Task<(List<QuestionBankSubject> Sources, QuestionBankSubject Target)> LoadAndValidateAsync(
            string operation, List<Guid>? rawSourceIds, Guid targetId, CancellationToken ct, bool tracked = false)
        {
            var sourceIds = (rawSourceIds ?? new List<Guid>()).Where(i => i != Guid.Empty).Distinct().ToList();
            if (sourceIds.Count == 0)
                throw new SubjectOpException(400, "NO_SOURCE", operation == "merge" ? "Choose at least one subject to merge." : "Choose the subject to move content from.");
            if (targetId == Guid.Empty)
                throw new SubjectOpException(400, "NO_TARGET", "Choose the subject to move content into.");
            if (sourceIds.Contains(targetId))
                throw new SubjectOpException(400, "SOURCE_IS_TARGET", "The target subject can't also be one of the source subjects.");
            if (sourceIds.Count > MaxMergeSources)
                throw new SubjectOpException(400, "TOO_MANY_SOURCES", $"You can merge at most {MaxMergeSources} subjects at once.");
            if (operation == "reassign" && sourceIds.Count != 1)
                throw new SubjectOpException(400, "ONE_SOURCE_ONLY", "Reassign moves content from exactly one subject.");

            var wanted = sourceIds.Append(targetId).ToList();
            var query = tracked ? _db.QuestionBankSubjects.AsQueryable() : _db.QuestionBankSubjects.AsNoTracking();
            var found = await query.Where(s => wanted.Contains(s.Id)).ToListAsync(ct);

            var target = found.FirstOrDefault(s => s.Id == targetId)
                ?? throw new SubjectOpException(404, "TARGET_NOT_FOUND", "The target subject no longer exists. Please refresh and pick another.");
            var sources = sourceIds.Select(i => found.FirstOrDefault(s => s.Id == i)).ToList();
            if (sources.Any(s => s == null))
                throw new SubjectOpException(404, "SOURCE_NOT_FOUND", "One of the source subjects no longer exists. Please refresh and try again.");

            return (sources!.Cast<QuestionBankSubject>().ToList(), target);
        }

        // Legacy paper questions of Published papers whose text subject matches -- exactly the ones that live
        // in the search index (unpublished papers must never be indexed).
        private Task<List<Guid>> PublishedPaperIdsForSubjectTextAsync(List<string> legacyKeys, CancellationToken ct) =>
            _db.Questions
                .Where(q => q.PaperId != null && q.Paper!.Status == PaperStatus.Published && legacyKeys.Contains(q.Subject.Trim().ToLower()))
                .Select(q => q.PaperId!.Value)
                .Distinct()
                .ToListAsync(ct);

        // The search index stores each legacy question's Subject text, so re-index the affected published
        // papers after a rename/merge. Best-effort -- same "log and carry on" contract as PapersController's
        // own indexing: the SQL fallback search always reads live data, so a failure here never breaks anything.
        private async Task ReindexPapersAsync(List<Guid> paperIds)
        {
            if (paperIds.Count == 0) return;
            try
            {
                if (_jobQueue.IsAvailable)
                {
                    foreach (var paperId in paperIds)
                        await _jobQueue.EnqueueSearchIndexJobAsync(new SearchIndexJob { JobType = SearchIndexJobType.IndexPaper, PaperId = paperId });
                    return;
                }

                foreach (var chunk in paperIds.Chunk(20))
                {
                    var docs = await _db.Questions
                        .Include(q => q.Paper).ThenInclude(p => p!.Exam)
                        .Where(q => q.PaperId != null && chunk.Contains(q.PaperId.Value))
                        .ToListAsync();
                    await _instantSearch.IndexQuestionsAsync(docs.Select(QuestionSearchDocument.FromQuestion));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Subject change committed, but re-indexing {Count} paper(s) into the search index failed.", paperIds.Count);
            }
        }

        // Audit rows are added to the SAME context/transaction as the change they describe -- if the audit
        // insert failed, the change rolls back rather than happening un-audited. (IAuditLogService swallows its
        // own failures and saves separately, which is right for low-stakes actions but not for a bulk rewrite.)
        private void Audit(Guid adminId, string action, Guid targetId, string detail)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                AdminId = adminId,
                Action = action,
                TargetType = "Subject",
                TargetId = targetId,
                Detail = detail.Length <= 1000 ? detail : detail[..997] + "..."
            });
        }

        private async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                // A retry re-runs this whole delegate, so start from a clean tracker every time.
                _db.ChangeTracker.Clear();
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    var result = await work();
                    await tx.CommitAsync(ct);
                    return result;
                }
                catch
                {
                    // Any failure at all => nothing from this operation is kept (no partial merges).
                    await tx.RollbackAsync(CancellationToken.None);
                    throw;
                }
            });
        }

        // Turns database-level failures into friendly messages (no SQL text ever reaches the admin UI).
        private async Task RunGuardedAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                throw new SubjectOpException(409, "DUPLICATE_SUBJECT", "That would create a duplicate name. Please refresh and try again.");
            }
            catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
            {
                throw new SubjectOpException(409, "SUBJECT_IN_USE", "This subject is still referenced by other records, so the change couldn't be completed. Nothing was changed.");
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Subject management database update failed.");
                throw new SubjectOpException(500, "DATABASE_ERROR", "The database couldn't complete this change, so nothing was changed. Please try again.");
            }
        }

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);

        private static bool IsForeignKeyViolation(DbUpdateException ex) =>
            ex.InnerException is SqlException sql && sql.Number == 547;
    }
}
