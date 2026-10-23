using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    public interface IExamTaxonomyService
    {
        // ---- admin ----
        Task<List<ExamTaxonomyExamSummaryDto>> ListExamsAsync(string? search, CancellationToken ct);
        Task<ExamTaxonomyOverviewDto> GetOverviewAsync(Guid examId, string? search, string? status, CancellationToken ct);

        Task<ExamTaxonomyResultDto> CreateSubjectAsync(Guid adminId, Guid examId, ExamSubjectCreateDto dto, CancellationToken ct);
        Task<ExamTaxonomyResultDto> RenameSubjectAsync(Guid adminId, Guid examId, Guid id, ExamTaxonomyRenameDto dto, CancellationToken ct);
        Task<ExamTaxonomyResultDto> SetSubjectActiveAsync(Guid adminId, Guid examId, Guid id, ExamTaxonomySetActiveDto dto, CancellationToken ct);
        Task<ExamTaxonomyUsageDto> GetSubjectUsageAsync(Guid examId, Guid id, CancellationToken ct);
        Task<ExamTaxonomyDeletePreviewDto> PreviewDeleteSubjectAsync(Guid examId, Guid id, CancellationToken ct);
        Task<ExamTaxonomyResultDto> DeleteSubjectAsync(Guid adminId, Guid examId, Guid id, string? mode, bool confirm, string? confirmName, int? expectedQuestions, CancellationToken ct);

        Task<ExamTopicListDto> ListTopicsAsync(Guid examId, Guid subjectId, string? search, string? status, CancellationToken ct);
        Task<ExamTaxonomyResultDto> CreateTopicAsync(Guid adminId, Guid examId, Guid subjectId, ExamTopicCreateDto dto, CancellationToken ct);
        Task<ExamTaxonomyResultDto> RenameTopicAsync(Guid adminId, Guid examId, Guid id, ExamTaxonomyRenameDto dto, CancellationToken ct);
        Task<ExamTaxonomyResultDto> SetTopicActiveAsync(Guid adminId, Guid examId, Guid id, ExamTaxonomySetActiveDto dto, CancellationToken ct);
        Task<ExamTaxonomyUsageDto> GetTopicUsageAsync(Guid examId, Guid id, CancellationToken ct);
        Task<ExamTaxonomyDeletePreviewDto> PreviewDeleteTopicAsync(Guid examId, Guid id, CancellationToken ct);
        Task<ExamTaxonomyResultDto> DeleteTopicAsync(Guid adminId, Guid examId, Guid id, string? mode, bool confirm, string? confirmName, int? expectedQuestions, CancellationToken ct);

        Task<ExamTaxonomyQuestionListDto> ListQuestionsAsync(Guid examId, ExamTaxonomyQuestionQueryDto query, CancellationToken ct);
        Task<ExamTaxonomyResultDto> MapQuestionsAsync(Guid adminId, Guid examId, ExamTaxonomyMapRequestDto dto, CancellationToken ct);
        Task<ExamTaxonomyResultDto> UnmapQuestionsAsync(Guid adminId, Guid examId, ExamTaxonomyUnmapRequestDto dto, CancellationToken ct);
        Task<List<QuestionExamClassificationDto>> GetQuestionClassificationsAsync(Guid questionId, CancellationToken ct);

        Task<ExamTaxonomyInitPreviewDto> PreviewInitializeAsync(Guid examId, CancellationToken ct);
        Task<ExamTaxonomyResultDto> InitializeAsync(Guid adminId, Guid examId, ExamTaxonomyInitRequestDto dto, CancellationToken ct);

        // ---- student-facing helpers (used by QuestionBankController; all additive) ----
        Task<bool> IsInitializedAsync(Guid examId, CancellationToken ct = default);
        Task<List<ExamSubjectFilterDto>> GetStudentSubjectsAsync(Guid examId, IQueryable<QuestionBankQuestion> visibleQuestions, CancellationToken ct = default);
        Task<List<ExamTopicFilterDto>> GetStudentTopicsAsync(Guid examId, Guid? examSubjectId, IQueryable<QuestionBankQuestion> visibleQuestions, CancellationToken ct = default);
        IQueryable<QuestionBankQuestion> FilterByExamSubjects(IQueryable<QuestionBankQuestion> source, IReadOnlyCollection<Guid> examSubjectIds);
        IQueryable<QuestionBankQuestion> FilterByExamTopics(IQueryable<QuestionBankQuestion> source, IReadOnlyCollection<Guid> examTopicIds);
        Task<Dictionary<Guid, (string? Subject, string? Topic)>> ResolveNamesAsync(Guid examId, IReadOnlyCollection<(Guid Id, Guid SubjectId, Guid TopicId)> questions, CancellationToken ct = default);
    }

    // See Models/ExamTaxonomyModels.cs for the data model and the resolution rule (explicit classification
    // row, else legacy fallback through the Base* lineage link). Nothing in this service ever creates, copies,
    // edits or deletes a question, paper, test, quiz, attempt or answer, and it never writes to the GLOBAL
    // QuestionBankSubject/QuestionBankTopic tables -- it only reads them.
    public class ExamTaxonomyService : IExamTaxonomyService
    {
        private const int SubjectNameMax = 100;
        private const int TopicNameMax = 150;
        private const int MaxMapBatch = 1000;

        private readonly ScoramDbContext _db;
        private readonly ILogger<ExamTaxonomyService> _logger;

        public ExamTaxonomyService(ScoramDbContext db, ILogger<ExamTaxonomyService> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ------------------------------------------------------------------------------
        // Small helpers
        // ------------------------------------------------------------------------------

        private static string NormalizeName(string? raw) => SubjectManagementService.NormalizeName(raw);
        private static string Key(string? raw) => SubjectManagementService.NameKey(raw);
        private static string VersionOf(DateTime createdAt, DateTime? updatedAt) => (updatedAt ?? createdAt).Ticks.ToString();

        private static SubjectOpException Err(int status, string code, string message, object? data = null) =>
            new(status, code, message, data);

        private async Task<Exam> LoadExamAsync(Guid examId, CancellationToken ct)
        {
            var exam = await _db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == examId, ct);
            return exam ?? throw Err(404, "EXAM_NOT_FOUND", "That exam doesn't exist.");
        }

        private async Task<ExamSubject> LoadSubjectAsync(Guid examId, Guid id, CancellationToken ct)
        {
            var s = await _db.ExamSubjects.FirstOrDefaultAsync(x => x.Id == id && x.ExamId == examId, ct);
            return s ?? throw Err(404, "SUBJECT_NOT_FOUND", "That subject doesn't exist for this exam.");
        }

        private async Task<(ExamTopic Topic, ExamSubject Subject)> LoadTopicAsync(Guid examId, Guid id, CancellationToken ct)
        {
            var t = await _db.ExamTopics.FirstOrDefaultAsync(x => x.Id == id, ct);
            var s = t == null ? null : await _db.ExamSubjects.FirstOrDefaultAsync(x => x.Id == t.ExamSubjectId && x.ExamId == examId, ct);
            if (t == null || s == null) throw Err(404, "TOPIC_NOT_FOUND", "That topic doesn't exist for this exam.");
            return (t, s);
        }

        private static string ValidateName(string? raw, int max, string what)
        {
            var name = NormalizeName(raw);
            if (name.Length == 0) throw Err(400, "NAME_REQUIRED", $"{what} name is required.");
            if (name.Length > max) throw Err(400, "NAME_TOO_LONG", $"{what} name can be at most {max} characters.");
            return name;
        }

        // Questions tagged to the exam (Question Bank, any year).
        private IQueryable<QuestionBankQuestion> ExamQuestions(Guid examId) =>
            _db.QuestionBankQuestions.Where(q => _db.QuestionBankExamMappings.Any(m => m.QuestionBankQuestionId == q.Id && m.ExamId == examId));

        // Ids of questions currently classified under an exam subject (explicit rows + legacy fallback).
        private IQueryable<Guid> SubjectQuestionIds(ExamSubject es)
        {
            var examId = es.ExamId;
            var esId = es.Id;
            var explicitIds = _db.QuestionBankExamClassifications
                .Where(c => c.ExamId == examId && c.ExamSubjectId == esId
                    && _db.QuestionBankExamMappings.Any(m => m.QuestionBankQuestionId == c.QuestionBankQuestionId && m.ExamId == examId))
                .Select(c => c.QuestionBankQuestionId);
            if (es.BaseSubjectId == null) return explicitIds;
            var baseId = es.BaseSubjectId.Value;
            var fallback = ExamQuestions(examId)
                .Where(q => q.SubjectId == baseId
                    && !_db.QuestionBankExamClassifications.Any(c => c.QuestionBankQuestionId == q.Id && c.ExamId == examId))
                .Select(q => q.Id);
            return explicitIds.Union(fallback);
        }

        // Ids of questions classified under an exam topic (explicit rows + legacy fallback).
        private IQueryable<Guid> TopicQuestionIds(ExamTopic et, ExamSubject es)
        {
            var examId = es.ExamId;
            var etId = et.Id;
            var explicitIds = _db.QuestionBankExamClassifications
                .Where(c => c.ExamId == examId && c.ExamTopicId == etId
                    && _db.QuestionBankExamMappings.Any(m => m.QuestionBankQuestionId == c.QuestionBankQuestionId && m.ExamId == examId))
                .Select(c => c.QuestionBankQuestionId);
            if (es.BaseSubjectId == null || et.BaseTopicId == null) return explicitIds;
            var baseSubject = es.BaseSubjectId.Value;
            var baseTopic = et.BaseTopicId.Value;
            var fallback = ExamQuestions(examId)
                .Where(q => q.SubjectId == baseSubject && q.TopicId == baseTopic
                    && !_db.QuestionBankExamClassifications.Any(c => c.QuestionBankQuestionId == q.Id && c.ExamId == examId))
                .Select(q => q.Id);
            return explicitIds.Union(fallback);
        }

        // ------------------------------------------------------------------------------
        // Resolution (explicit row, else legacy fallback) -- in memory over a small taxonomy
        // ------------------------------------------------------------------------------

        private sealed class Taxonomy
        {
            public Dictionary<Guid, ExamSubject> Subjects { get; init; } = new();
            public Dictionary<Guid, ExamTopic> Topics { get; init; } = new();
            public Dictionary<Guid, ExamSubject> SubjectByBase { get; init; } = new();
            public Dictionary<(Guid Subject, Guid BaseTopic), ExamTopic> TopicByBase { get; init; } = new();

            public (Guid? S, Guid? T) Resolve(bool has, Guid? s, Guid? t, Guid globalSubject, Guid globalTopic)
            {
                if (has) return (s, t);
                if (!SubjectByBase.TryGetValue(globalSubject, out var sub)) return (null, null);
                TopicByBase.TryGetValue((sub.Id, globalTopic), out var top);
                return (sub.Id, top?.Id);
            }
        }

        private async Task<Taxonomy> LoadTaxonomyAsync(Guid examId, CancellationToken ct)
        {
            var subjects = await _db.ExamSubjects.AsNoTracking().Where(s => s.ExamId == examId).ToListAsync(ct);
            var topics = await _db.ExamTopics.AsNoTracking()
                .Where(t => _db.ExamSubjects.Any(s => s.Id == t.ExamSubjectId && s.ExamId == examId)).ToListAsync(ct);
            var tax = new Taxonomy
            {
                Subjects = subjects.ToDictionary(s => s.Id),
                Topics = topics.ToDictionary(t => t.Id),
            };
            foreach (var s in subjects.Where(s => s.BaseSubjectId != null).OrderBy(s => s.CreatedAt))
                tax.SubjectByBase.TryAdd(s.BaseSubjectId!.Value, s);
            foreach (var t in topics.Where(t => t.BaseTopicId != null).OrderBy(t => t.CreatedAt))
                tax.TopicByBase.TryAdd((t.ExamSubjectId, t.BaseTopicId!.Value), t);
            return tax;
        }

        private sealed record Combo(Guid SubjectId, Guid TopicId, bool Has, Guid? S, Guid? T, int Count);

        // One grouped query: distinct (global subject/topic, classification) combinations among the questions
        // of `source` that are tagged to the exam, with counts. Small result regardless of exam size.
        private async Task<List<Combo>> CombosAsync(Guid examId, IQueryable<QuestionBankQuestion> source, CancellationToken ct)
        {
            var rows = await (
                from q in source.Where(q => _db.QuestionBankExamMappings.Any(m => m.QuestionBankQuestionId == q.Id && m.ExamId == examId))
                join c in _db.QuestionBankExamClassifications.Where(c => c.ExamId == examId)
                    on q.Id equals c.QuestionBankQuestionId into cj
                from c in cj.DefaultIfEmpty()
                select new
                {
                    q.SubjectId,
                    q.TopicId,
                    Has = c != null,
                    S = c != null ? c.ExamSubjectId : (Guid?)null,
                    T = c != null ? c.ExamTopicId : (Guid?)null
                })
                .GroupBy(x => new { x.SubjectId, x.TopicId, x.Has, x.S, x.T })
                .Select(g => new { g.Key.SubjectId, g.Key.TopicId, g.Key.Has, g.Key.S, g.Key.T, Count = g.Count() })
                .ToListAsync(ct);
            return rows.Select(r => new Combo(r.SubjectId, r.TopicId, r.Has, r.S, r.T, r.Count)).ToList();
        }

        private sealed class Counts
        {
            public Dictionary<Guid, int> BySubject { get; } = new();
            public Dictionary<Guid, int> ByTopic { get; } = new();
            public int Total { get; set; }
            public int Unassigned { get; set; }
        }

        private async Task<Counts> CountAsync(Guid examId, IQueryable<QuestionBankQuestion> source, Taxonomy tax, CancellationToken ct)
        {
            var counts = new Counts();
            foreach (var c in await CombosAsync(examId, source, ct))
            {
                counts.Total += c.Count;
                var (s, t) = tax.Resolve(c.Has, c.S, c.T, c.SubjectId, c.TopicId);
                if (s == null) { counts.Unassigned += c.Count; continue; }
                counts.BySubject[s.Value] = counts.BySubject.GetValueOrDefault(s.Value) + c.Count;
                if (t != null) counts.ByTopic[t.Value] = counts.ByTopic.GetValueOrDefault(t.Value) + c.Count;
            }
            return counts;
        }

        // ------------------------------------------------------------------------------
        // Mapping helpers
        // ------------------------------------------------------------------------------

        private ExamSubjectItemDto ToItem(ExamSubject s, string? baseName, int topicCount, int activeTopicCount, int questions) => new()
        {
            Id = s.Id, ExamId = s.ExamId, BaseSubjectId = s.BaseSubjectId, BaseSubjectName = baseName,
            Name = s.Name, IsActive = s.IsActive, TopicCount = topicCount, ActiveTopicCount = activeTopicCount,
            QuestionCount = questions, CreatedAt = s.CreatedAt, UpdatedAt = s.UpdatedAt, Version = VersionOf(s.CreatedAt, s.UpdatedAt)
        };

        private ExamTopicItemDto ToItem(ExamTopic t, string? baseName, int questions) => new()
        {
            Id = t.Id, ExamSubjectId = t.ExamSubjectId, BaseTopicId = t.BaseTopicId, BaseTopicName = baseName,
            Name = t.Name, IsActive = t.IsActive, QuestionCount = questions, CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt, Version = VersionOf(t.CreatedAt, t.UpdatedAt)
        };

        private async Task<Dictionary<Guid, string>> GlobalSubjectNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
        {
            var list = ids.Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
            if (list.Count == 0) return new();
            return await _db.QuestionBankSubjects.AsNoTracking().Where(s => list.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        }

        private async Task<Dictionary<Guid, string>> GlobalTopicNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
        {
            var list = ids.Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
            if (list.Count == 0) return new();
            return await _db.QuestionBankTopics.AsNoTracking().Where(s => list.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        }

        // ------------------------------------------------------------------------------
        // Exams + overview
        // ------------------------------------------------------------------------------

        public async Task<List<ExamTaxonomyExamSummaryDto>> ListExamsAsync(string? search, CancellationToken ct)
        {
            var q = _db.Exams.AsNoTracking().AsQueryable();
            var term = NormalizeName(search);
            if (term.Length > 0) q = q.Where(e => e.Name.Contains(term) || (e.BusinessId != null && e.BusinessId.Contains(term)));
            var exams = await q.OrderBy(e => e.Name).ToListAsync(ct);

            var qCounts = await _db.QuestionBankExamMappings
                .GroupBy(m => m.ExamId)
                .Select(g => new { ExamId = g.Key, Count = g.Select(x => x.QuestionBankQuestionId).Distinct().Count() })
                .ToDictionaryAsync(x => x.ExamId, x => x.Count, ct);
            var sCounts = await _db.ExamSubjects
                .GroupBy(s => s.ExamId)
                .Select(g => new { ExamId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ExamId, x => x.Count, ct);

            return exams.Select(e => new ExamTaxonomyExamSummaryDto
            {
                Id = e.Id, BusinessId = e.BusinessId, Name = e.Name,
                QuestionCount = qCounts.GetValueOrDefault(e.Id),
                SubjectCount = sCounts.GetValueOrDefault(e.Id),
                Initialized = sCounts.GetValueOrDefault(e.Id) > 0
            }).ToList();
        }

        public async Task<ExamTaxonomyOverviewDto> GetOverviewAsync(Guid examId, string? search, string? status, CancellationToken ct)
        {
            var exam = await LoadExamAsync(examId, ct);
            var tax = await LoadTaxonomyAsync(examId, ct);
            var counts = await CountAsync(examId, _db.QuestionBankQuestions, tax, ct);

            var baseNames = await GlobalSubjectNamesAsync(tax.Subjects.Values.Select(s => s.BaseSubjectId), ct);
            var topicsBySubject = tax.Topics.Values.GroupBy(t => t.ExamSubjectId).ToDictionary(g => g.Key, g => g.ToList());

            IEnumerable<ExamSubject> subjects = tax.Subjects.Values;
            var term = Key(search);
            if (term.Length > 0) subjects = subjects.Where(s => s.Name.ToLowerInvariant().Contains(term));
            subjects = (status ?? "all").ToLowerInvariant() switch
            {
                "active" => subjects.Where(s => s.IsActive),
                "archived" or "inactive" => subjects.Where(s => !s.IsActive),
                _ => subjects
            };

            var items = subjects.OrderBy(s => s.Name).Select(s =>
            {
                var tops = topicsBySubject.GetValueOrDefault(s.Id) ?? new List<ExamTopic>();
                return ToItem(s, s.BaseSubjectId != null ? baseNames.GetValueOrDefault(s.BaseSubjectId.Value) : null,
                    tops.Count, tops.Count(t => t.IsActive), counts.BySubject.GetValueOrDefault(s.Id));
            }).ToList();

            return new ExamTaxonomyOverviewDto
            {
                Exam = new ExamTaxonomyExamSummaryDto
                {
                    Id = exam.Id, BusinessId = exam.BusinessId, Name = exam.Name,
                    QuestionCount = counts.Total, SubjectCount = tax.Subjects.Count, Initialized = tax.Subjects.Count > 0
                },
                Initialized = tax.Subjects.Count > 0,
                TotalQuestions = counts.Total,
                UnassignedQuestions = counts.Unassigned,
                Subjects = items
            };
        }

        // ------------------------------------------------------------------------------
        // Subjects
        // ------------------------------------------------------------------------------

        public async Task<ExamTaxonomyResultDto> CreateSubjectAsync(Guid adminId, Guid examId, ExamSubjectCreateDto dto, CancellationToken ct)
        {
            var exam = await LoadExamAsync(examId, ct);
            var name = ValidateName(dto.Name, SubjectNameMax, "Subject");

            ExamSubject created = null!;
            string? baseName = null;
            await RunGuardedAsync(() => InTransactionAsync(async () =>
            {
                var existing = await _db.ExamSubjects.Where(s => s.ExamId == examId).ToListAsync(ct);

                // Legacy questions exist but the exam hasn't been initialised: creating a lone subject now would
                // make the student filter (which switches to the exam taxonomy as soon as one subject exists)
                // hide every existing question. Make the admin initialise first, deliberately.
                if (existing.Count == 0 && await ExamQuestions(examId).AnyAsync(ct))
                    throw Err(409, "TAXONOMY_NOT_INITIALIZED",
                        $"'{exam.Name}' already has questions classified by the global subjects. Initialise its subject/topic list from that classification first, then add or rename subjects.");

                if (existing.Any(s => s.IsActive && Key(s.Name) == Key(name)))
                    throw Err(409, "DUPLICATE_SUBJECT", $"'{name}' already exists in {exam.Name}.");
                var archivedSame = existing.FirstOrDefault(s => !s.IsActive && Key(s.Name) == Key(name));
                if (archivedSame != null)
                    throw Err(409, "ARCHIVED_SUBJECT_EXISTS", $"'{name}' exists in {exam.Name} but is archived. Restore it instead of creating a duplicate.", new { id = archivedSame.Id });

                if (dto.BaseSubjectId.HasValue)
                {
                    var g = await _db.QuestionBankSubjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == dto.BaseSubjectId.Value, ct)
                        ?? throw Err(400, "BASE_SUBJECT_NOT_FOUND", "The linked global subject doesn't exist.");
                    var linked = existing.FirstOrDefault(s => s.BaseSubjectId == g.Id);
                    if (linked != null)
                        throw Err(409, "BASE_ALREADY_LINKED", $"This exam already has '{linked.Name}' linked to the global subject '{g.Name}'.", new { id = linked.Id });
                    baseName = g.Name;
                }

                created = new ExamSubject { ExamId = examId, BaseSubjectId = dto.BaseSubjectId, Name = name, CreatedByAdminId = adminId };
                _db.ExamSubjects.Add(created);
                Audit(adminId, "ExamSubject.Create", created.Id, $"[{exam.Name}] Created subject '{name}'.");
                await _db.SaveChangesAsync(ct);
                return true;
            }, ct));

            return new ExamTaxonomyResultDto { Message = $"Subject '{name}' added to {exam.Name}.", Subject = ToItem(created, baseName, 0, 0, 0) };
        }

        public async Task<ExamTaxonomyResultDto> RenameSubjectAsync(Guid adminId, Guid examId, Guid id, ExamTaxonomyRenameDto dto, CancellationToken ct)
        {
            var exam = await LoadExamAsync(examId, ct);
            var name = ValidateName(dto.Name, SubjectNameMax, "Subject");
            string oldName = string.Empty;
            ExamSubject subject = null!;

            await RunGuardedAsync(() => InTransactionAsync(async () =>
            {
                subject = await LoadSubjectAsync(examId, id, ct);
                if (dto.Version != null && dto.Version != VersionOf(subject.CreatedAt, subject.UpdatedAt))
                    throw Err(409, "CONCURRENT_MODIFICATION", "This subject was changed by someone else. Refresh and try again.");
                oldName = subject.Name;
                if (oldName == name) return true; // nothing to do

                if (subject.IsActive && await _db.ExamSubjects.AnyAsync(s => s.ExamId == examId && s.Id != id && s.IsActive && s.Name.ToLower() == name.ToLower(), ct))
                    throw Err(409, "DUPLICATE_SUBJECT", $"'{name}' already exists in {exam.Name}.");

                // Only this exam subject's own Name changes. Same row/Id, so every classification, paper, test,
                // attempt and result that refers to it is untouched, and no other exam is read or written.
                subject.Name = name;
                subject.UpdatedAt = DateTime.UtcNow;
                Audit(adminId, "ExamSubject.Rename", subject.Id, $"[{exam.Name}] Renamed subject '{oldName}' -> '{name}'.");
                await _db.SaveChangesAsync(ct);
                return true;
            }, ct));

            return new ExamTaxonomyResultDto
            {
                Message = oldName == name ? "The name is unchanged." : $"Renamed '{oldName}' to '{name}' in {exam.Name} only.",
                Subject = await ItemForSubjectAsync(subject, ct)
            };
        }

        public async Task<ExamTaxonomyResultDto> SetSubjectActiveAsync(Guid adminId, Guid examId, Guid id, ExamTaxonomySetActiveDto dto, CancellationToken ct)
        {
            var exam = await LoadExamAsync(examId, ct);
            ExamSubject subject = null!;
            await RunGuardedAsync(() => InTransactionAsync(async () =>
            {
                subject = await LoadSubjectAsync(examId, id, ct);
                if (dto.Version != null && dto.Version != VersionOf(subject.CreatedAt, subject.UpdatedAt))
                    throw Err(409, "CONCURRENT_MODIFICATION", "This subject was changed by someone else. Refresh and try again.");
                if (subject.IsActive == dto.IsActive) return true;
                if (dto.IsActive && await _db.ExamSubjects.AnyAsync(s => s.ExamId == examId && s.Id != id && s.IsActive && s.Name.ToLower() == subject.Name.ToLower(), ct))
                    throw Err(409, "DUPLICATE_SUBJECT", $"Another active subject called '{subject.Name}' exists in {exam.Name}. Rename one of them first.");
                subject.IsActive = dto.IsActive;
                subject.UpdatedAt = DateTime.UtcNow;
                Audit(adminId, dto.IsActive ? "ExamSubject.Restore" : "ExamSubject.Archive", subject.Id,
                    $"[{exam.Name}] {(dto.IsActive ? "Restored" : "Archived")} subject '{subject.Name}'.");
                await _db.SaveChangesAsync(ct);
                return true;
            }, ct));
            return new ExamTaxonomyResultDto
            {
                Message = dto.IsActive ? $"'{subject.Name}' restored." : $"'{subject.Name}' archived. Its questions stay mapped; it is hidden from filters.",
                Subject = await ItemForSubjectAsync(subject, ct)
            };
        }

        private async Task<ExamSubjectItemDto> ItemForSubjectAsync(ExamSubject s, CancellationToken ct)
        {
            var tax = await LoadTaxonomyAsync(s.ExamId, ct);
            var counts = await CountAsync(s.ExamId, _db.QuestionBankQuestions, tax, ct);
            var tops = tax.Topics.Values.Where(t => t.ExamSubjectId == s.Id).ToList();
            var baseNames = await GlobalSubjectNamesAsync(new Guid?[] { s.BaseSubjectId }, ct);
            return ToItem(s, s.BaseSubjectId != null ? baseNames.GetValueOrDefault(s.BaseSubjectId.Value) : null,
                tops.Count, tops.Count(t => t.IsActive), counts.BySubject.GetValueOrDefault(s.Id));
        }

        private async Task<ExamTaxonomyUsageDto> UsageAsync(Exam exam, IQueryable<Guid> ids, CancellationToken ct)
        {
            var examId = exam.Id;
            var examName = exam.Name;
            var u = new ExamTaxonomyUsageDto { Questions = await ids.CountAsync(ct) };
            if (u.Questions == 0) return u;

            var paperLinks = _db.PaperQuestionBankLinks
                .Where(l => ids.Contains(l.QuestionBankQuestionId) && _db.Papers.Any(p => p.Id == l.PaperId && p.ExamId == examId))
                .Select(l => l.PaperId);
            var paperMirrors = _db.Questions
                .Where(q => q.PaperId != null && q.MirroredToQuestionBankQuestionId != null && ids.Contains(q.MirroredToQuestionBankQuestionId.Value)
                    && _db.Papers.Any(p => p.Id == q.PaperId && p.ExamId == examId))
                .Select(q => q.PaperId!.Value);
            u.Papers = await paperLinks.Union(paperMirrors).CountAsync(ct);

            u.MockTests = await _db.MockTestQuestions
                .Where(mq => mq.QuestionBankQuestionId != null && ids.Contains(mq.QuestionBankQuestionId.Value)
                    && _db.MockTests.Any(t => t.Id == mq.MockTestId && (t.ExamId == examId || (t.ExamId == null && t.ExamName == examName))))
                .Select(mq => mq.MockTestId).Distinct().CountAsync(ct);

            u.PracticeTests = await _db.PracticeTestTemplateQuestions
                .Where(pq => pq.QuestionBankQuestionId != null && ids.Contains(pq.QuestionBankQuestionId.Value)
                    && _db.PracticeTestTemplates.Any(t => t.Id == pq.PracticeTestTemplateId && t.ExamId == examId))
                .Select(pq => pq.PracticeTestTemplateId).Distinct().CountAsync(ct);

            // Quizzes aren't tied to an exam, so this counts every quiz that contains one of the questions.
            u.Quizzes = await _db.QuizQuestions
                .Where(qq => ids.Contains(qq.QuestionBankQuestionId))
                .Select(qq => qq.QuizId).Distinct().CountAsync(ct);

            u.StudentAnswers = await _db.StudentAnswers
                .CountAsync(a => a.QuestionBankQuestionId != null && ids.Contains(a.QuestionBankQuestionId.Value), ct);
            return u;
        }

        public async Task<ExamTaxonomyUsageDto> GetSubjectUsageAsync(Guid examId, Guid id, CancellationToken ct)
        {
            var exam = await LoadExamAsync(examId, ct);
            var s = await LoadSubjectAsync(examId, id, ct);
            var u = await UsageAsync(exam, SubjectQuestionIds(s), ct);
            var tops = await _db.ExamTopics.Where(t => t.ExamSubjectId == id).ToListAsync(ct);
            u.Topics = tops.Count;
            u.ActiveTopics = tops.Count(t => t.IsActive);
            return u;
        }

        public async Task<ExamTaxonomyDeletePreviewDto> PreviewDeleteSubjectAsync(Guid examId, Guid id, CancellationToken ct)
        {
            var s = await LoadSubjectAsync(examId, id, ct);
            var u = await GetSubjectUsageAsync(examId, id, ct);
            return BuildDeletePreview("subject", s.Id, s.Name, s.IsActive, u);
        }

        private static ExamTaxonomyDeletePreviewDto BuildDeletePreview(string kind, Guid id, string name, bool isActive, ExamTaxonomyUsageDto u)
        {
            var blockers = new List<string>();
            if (u.Topics > 0) blockers.Add($"{u.Topics:N0} topic(s) belong to this subject");
            if (u.Questions > 0) blockers.Add($"{u.Questions:N0} question(s) are classified under it");
            if (u.Papers > 0) blockers.Add($"{u.Papers:N0} paper(s) of this exam contain those questions");
            if (u.MockTests > 0) blockers.Add($"{u.MockTests:N0} mock test(s) of this exam contain those questions");
            if (u.PracticeTests > 0) blockers.Add($"{u.PracticeTests:N0} practice test(s) of this exam contain those questions");
            if (u.Quizzes > 0) blockers.Add($"{u.Quizzes:N0} quiz(zes) contain those questions");
            if (u.StudentAnswers > 0) blockers.Add($"{u.StudentAnswers:N0} recorded student answer(s) are on those questions (history is never changed)");

            var canDelete = u.Topics == 0 && u.Questions == 0;
            var msg = canDelete
                ? $"Nothing depends on this {kind}; it can be deleted."
                : $"This {kind} is in use, so it can't simply be deleted. You can archive it (hide it, keep everything mapped), or unassign its questions and then remove it. No question, paper, test or result is ever deleted.";
            return new ExamTaxonomyDeletePreviewDto
            {
                Id = id, Kind = kind, Name = name, Usage = u, CanDelete = canDelete,
                CanArchive = isActive, CanUnassign = !canDelete, Blockers = blockers, Message = msg
            };
        }

        public async Task<ExamTaxonomyResultDto> DeleteSubjectAsync(Guid adminId, Guid examId, Guid id, string? mode, bool confirm, string? confirmName, int? expectedQuestions, CancellationToken ct)
        {
            var exam = await LoadExamAsync(examId, ct);
            var unassign = string.Equals(mode, "unassign", StringComparison.OrdinalIgnoreCase);
            if (!confirm) throw Err(400, "CONFIRMATION_REQUIRED", "Please confirm this action before continuing.");

            int unassigned = 0, topicsRemoved = 0;
            string name = string.Empty;

            await RunGuardedAsync(() => InTransactionAsync(async () =>
            {
                // Everything is re-checked against the LIVE data inside the transaction -- the preview may be old.
                var s = await LoadSubjectAsync(examId, id, ct);
                name = s.Name;
                if (Key(confirmName) != Key(s.Name))
                    throw Err(400, "CONFIRM_NAME_MISMATCH", $"To confirm, type the subject's name exactly: {s.Name}");

                var u = await GetSubjectUsageAsync(examId, id, ct);
                if (!unassign)
                {
                    if (u.Topics > 0 || u.Questions > 0)
                        throw Err(409, "SUBJECT_IN_USE", "This subject is still in use, so it was not deleted. Archive it, or unassign its questions first.",
                            BuildDeletePreview("subject", s.Id, s.Name, s.IsActive, u));
                }
                else if (expectedQuestions.HasValue && expectedQuestions.Value != u.Questions)
                {
                    throw Err(409, "IMPACT_CHANGED",
                        $"The affected questions changed since you opened the preview (it was {expectedQuestions.Value:N0}, it is now {u.Questions:N0}). Nothing was changed -- review the updated preview and confirm again.",
                        new { expected = expectedQuestions.Value, actual = u.Questions });
                }

                if (unassign)
                {
                    unassigned = await UnassignAsync(adminId, examId, SubjectQuestionIds(s), null, ct);
                    var topics = await _db.ExamTopics.Where(t => t.ExamSubjectId == id).ToListAsync(ct);
                    var topicIds = topics.Select(t => t.Id).ToList();
                    // Defensive: no classification may keep pointing at a topic that's about to be removed.
                    var strays = await _db.QuestionBankExamClassifications.Where(c => c.ExamTopicId != null && topicIds.Contains(c.ExamTopicId.Value)).ToListAsync(ct);
                    foreach (var c in strays) { c.ExamTopicId = null; c.UpdatedAt = DateTime.UtcNow; c.UpdatedByAdminId = adminId; }
                    await _db.SaveChangesAsync(ct);
                    _db.ExamTopics.RemoveRange(topics);
                    topicsRemoved = topics.Count;
                }

                _db.ExamSubjects.Remove(s);
                Audit(adminId, "ExamSubject.Delete", s.Id,
                    $"[{exam.Name}] Deleted subject '{s.Name}'" + (unassign ? $" after unassigning {unassigned:N0} question(s) and removing {topicsRemoved:N0} topic(s)." : " (it had no dependent content)."));
                await _db.SaveChangesAsync(ct);
                return true;
            }, ct));

            return new ExamTaxonomyResultDto
            {
                Message = unassign
                    ? $"Deleted '{name}' from {exam.Name}. {unassigned:N0} question(s) are now unassigned in this exam (the questions themselves are untouched)."
                    : $"Deleted '{name}' from {exam.Name}.",
                Affected = unassigned
            };
        }

        // Sets the classification of every question in `ids` to "no subject" (subject==null) or "keep subject,
        // drop topic" (keepSubject set). Creates an explicit row for legacy-fallback questions so they don't
        // silently re-attach. Runs inside the caller's transaction. Returns how many questions were touched.
        private async Task<int> UnassignAsync(Guid adminId, Guid examId, IQueryable<Guid> ids, Guid? keepSubject, CancellationToken ct)
        {
            var idList = await ids.ToListAsync(ct);
            if (idList.Count == 0) return 0;
            var existing = await _db.QuestionBankExamClassifications
                .Where(c => c.ExamId == examId && idList.Contains(c.QuestionBankQuestionId)).ToListAsync(ct);
            var byQ = existing.ToDictionary(c => c.QuestionBankQuestionId);
            var now = DateTime.UtcNow;
            foreach (var qid in idList)
            {
                if (!byQ.TryGetValue(qid, out var row))
                {
                    row = new QuestionBankExamClassification { QuestionBankQuestionId = qid, ExamId = examId, CreatedAt = now };
                    _db.QuestionBankExamClassifications.Add(row);
                }
                row.ExamSubjectId = keepSubject;
                row.ExamTopicId = null;
                row.UpdatedAt = now;
                row.UpdatedByAdminId = adminId;
            }
            await _db.SaveChangesAsync(ct);
            return idList.Count;
        }

        // ------------------------------------------------------------------------------
        // Topics
        // ------------------------------------------------------------------------------

        public async Task<ExamTopicListDto> ListTopicsAsync(Guid examId, Guid subjectId, string? search, string? status, CancellationToken ct)
        {
            await LoadExamAsync(examId, ct);
            var subject = await LoadSubjectAsync(examId, subjectId, ct);
            var tax = await LoadTaxonomyAsync(examId, ct);
            var counts = await CountAsync(examId, _db.QuestionBankQuestions, tax, ct);

            var tops = tax.Topics.Values.Where(t => t.ExamSubjectId == subjectId);
            var term = Key(search);
            if (term.Length > 0) tops = tops.Where(t => t.Name.ToLowerInvariant().Contains(term));
            tops = (status ?? "all").ToLowerInvariant() switch
            {
                "active" => tops.Where(t => t.IsActive),
                "archived" or "inactive" => tops.Where(t => !t.IsActive),
                _ => tops
            };
            var topicList = tops.OrderBy(t => t.Name).ToList();
            var baseNames = await GlobalTopicNamesAsync(topicList.Select(t => t.BaseTopicId), ct);

            var all = tax.Topics.Values.Where(t => t.ExamSubjectId == subjectId).ToList();
            var baseSubj = await GlobalSubjectNamesAsync(new Guid?[] { subject.BaseSubjectId }, ct);
            return new ExamTopicListDto
            {
                Subject = ToItem(subject, subject.BaseSubjectId != null ? baseSubj.GetValueOrDefault(subject.BaseSubjectId.Value) : null,
                    all.Count, all.Count(t => t.IsActive), counts.BySubject.GetValueOrDefault(subject.Id)),
                Topics = topicList.Select(t => ToItem(t, t.BaseTopicId != null ? baseNames.GetValueOrDefault(t.BaseTopicId.Value) : null,
                    counts.ByTopic.GetValueOrDefault(t.Id))).ToList()
            };
        }

        public async Task<ExamTaxonomyResultDto> CreateTopicAsync(Guid adminId, Guid examId, Guid subjectId, ExamTopicCreateDto dto, CancellationToken ct)
        {
            var exam = await LoadExamAsync(examId, ct);
            var name = ValidateName(dto.Name, TopicNameMax, "Topic");
            ExamTopic created = null!;
            string? baseName = null;
            await RunGuardedAsync(() => InTransactionAsync(async () =>
            {
                var subject = await LoadSubjectAsync(examId, subjectId, ct);
                if (!subject.IsActive) throw Err(400, "SUBJECT_ARCHIVED", $"'{subject.Name}' is archived. Restore it before adding topics.");
                var siblings = await _db.ExamTopics.Where(t => t.ExamSubjectId == subjectId).ToListAsync(ct);
                if (siblings.Any(t => t.IsActive && Key(t.Name) == Key(name)))
                    throw Err(409, "DUPLICATE_TOPIC", $"'{name}' already exists under {subject.Name}.");
                var archivedSame = siblings.FirstOrDefault(t => !t.IsActive && Key(t.Name) == Key(name));
                if (archivedSame != null)
                    throw Err(409, "ARCHIVED_TOPIC_EXISTS", $"'{name}' exists under {subject.Name} but is archived. Restore it instead.", new { id = archivedSame.Id });
                if (dto.BaseTopicId.HasValue)
                {
                    var g = await _db.QuestionBankTopics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.BaseTopicId.Value, ct)
                        ?? throw Err(400, "BASE_TOPIC_NOT_FOUND", "The linked global topic doesn't exist.");
                    if (siblings.Any(t => t.BaseTopicId == g.Id))
                        throw Err(409, "BASE_ALREADY_LINKED", $"This subject already has a topic linked to the global topic '{g.Name}'.");
                    baseName = g.Name;
                }
                created = new ExamTopic { ExamSubjectId = subjectId, BaseTopicId = dto.BaseTopicId, Name = name, CreatedByAdminId = adminId };
                _db.ExamTopics.Add(created);
                Audit(adminId, "ExamTopic.Create", created.Id, $"[{exam.Name} > {subject.Name}] Created topic '{name}'.");
                await _db.SaveChangesAsync(ct);
                return true;
            }, ct));
            return new ExamTaxonomyResultDto { Message = $"Topic '{name}' added.", Topic = ToItem(created, baseName, 0) };
        }

        public async Task<ExamTaxonomyResultDto> RenameTopicAsync(Guid adminId, Guid examId, Guid id, ExamTaxonomyRenameDto dto, CancellationToken ct)
        {
            var exam = await LoadExamAsync(examId, ct);
            var name = ValidateName(dto.Name, TopicNameMax, "Topic");
            string oldName = string.Empty;
            ExamTopic topic = null!;
            await RunGuardedAsync(() => InTransactionAsync(async () =>
            {
                var (t, s) = await LoadTopicAsync(examId, id, ct);
                topic = t;
                if (dto.Version != null && dto.Version != VersionOf(t.CreatedAt, t.UpdatedAt))
                    throw Err(409, "CONCURRENT_MODIFICATION", "This topic was changed by someone else. Refresh and try again.");
                oldName = t.Name;
                if (oldName == name) return true;
                // Duplicate rule matches the existing topic rule: unique within its subject (here: its exam subject).
                if (t.IsActive && await _db.ExamTopics.AnyAsync(x => x.ExamSubjectId == t.ExamSubjectId && x.Id != id && x.IsActive && x.Name.ToLower() == name.ToLower(), ct))
                    throw Err(409, "DUPLICATE_TOPIC", $"'{name}' already exists under {s.Name}.");
                t.Name = name;
                t.UpdatedAt = DateTime.UtcNow;
                Audit(adminId, "ExamTopic.Rename", t.Id, $"[{exam.Name} > {s.Name}] Renamed topic '{oldName}' -> '{name}'.");
                await _db.SaveChangesAsync(ct);
                return true;
            }, ct));
            return new ExamTaxonomyResultDto
            {
                Message = oldName == name ? "The name is unchanged." : $"Renamed '{oldName}' to '{name}' in {exam.Name} only.",
                Topic = await ItemForTopicAsync(topic, ct)
            };
        }

        public async Task<ExamTaxonomyResultDto> SetTopicActiveAsync(Guid adminId, Guid examId, Guid id, ExamTaxonomySetActiveDto dto, CancellationToken ct)
        {
            var exam = await LoadExamAsync(examId, ct);
            ExamTopic topic = null!;
            await RunGuardedAsync(() => InTransactionAsync(async () =>
            {
                var (t, s) = await LoadTopicAsync(examId, id, ct);
                topic = t;
                if (dto.Version != null && dto.Version != VersionOf(t.CreatedAt, t.UpdatedAt))
                    throw Err(409, "CONCURRENT_MODIFICATION", "This topic was changed by someone else. Refresh and try again.");
                if (t.IsActive == dto.IsActive) return true;
                if (dto.IsActive && !s.IsActive) throw Err(400, "SUBJECT_ARCHIVED", $"'{s.Name}' is archived. Restore it first.");
                if (dto.IsActive && await _db.ExamTopics.AnyAsync(x => x.ExamSubjectId == t.ExamSubjectId && x.Id != id && x.IsActive && x.Name.ToLower() == t.Name.ToLower(), ct))
                    throw Err(409, "DUPLICATE_TOPIC", $"Another active topic called '{t.Name}' exists under {s.Name}. Rename one first.");
                t.IsActive = dto.IsActive;
                t.UpdatedAt = DateTime.UtcNow;
                Audit(adminId, dto.IsActive ? "ExamTopic.Restore" : "ExamTopic.Archive", t.Id,
                    $"[{exam.Name} > {s.Name}] {(dto.IsActive ? "Restored" : "Archived")} topic '{t.Name}'.");
                await _db.SaveChangesAsync(ct);
                return true;
            }, ct));
            return new ExamTaxonomyResultDto
            {
                Message = dto.IsActive ? $"'{topic.Name}' restored." : $"'{topic.Name}' archived. Its questions stay mapped; it is hidden from filters.",
                Topic = await ItemForTopicAsync(topic, ct)
            };
        }

        private async Task<ExamTopicItemDto> ItemForTopicAsync(ExamTopic t, CancellationToken ct)
        {
            var s = await _db.ExamSubjects.AsNoTracking().FirstAsync(x => x.Id == t.ExamSubjectId, ct);
            var tax = await LoadTaxonomyAsync(s.ExamId, ct);
            var counts = await CountAsync(s.ExamId, _db.QuestionBankQuestions, tax, ct);
            var baseNames = await GlobalTopicNamesAsync(new Guid?[] { t.BaseTopicId }, ct);
            return ToItem(t, t.BaseTopicId != null ? baseNames.GetValueOrDefault(t.BaseTopicId.Value) : null, counts.ByTopic.GetValueOrDefault(t.Id));
        }

        public async Task<ExamTaxonomyUsageDto> GetTopicUsageAsync(Guid examId, Guid id, CancellationToken ct)
        {
            var exam = await LoadExamAsync(examId, ct);
            var (t, s) = await LoadTopicAsync(examId, id, ct);
            return await UsageAsync(exam, TopicQuestionIds(t, s), ct);
        }

        public async Task<ExamTaxonomyDeletePreviewDto> PreviewDeleteTopicAsync(Guid examId, Guid id, CancellationToken ct)
        {
            var (t, _) = await LoadTopicAsync(examId, id, ct);
            var u = await GetTopicUsageAsync(examId, id, ct);
            return BuildDeletePreview("topic", t.Id, t.Name, t.IsActive, u);
        }

        public async Task<ExamTaxonomyResultDto> DeleteTopicAsync(Guid adminId, Guid examId, Guid id, string? mode, bool confirm, string? confirmName, int? expectedQuestions, CancellationToken ct)
        {
            var exam = await LoadExamAsync(examId, ct);
            var unassign = string.Equals(mode, "unassign", StringComparison.OrdinalIgnoreCase);
            if (!confirm) throw Err(400, "CONFIRMATION_REQUIRED", "Please confirm this action before continuing.");
            int unassigned = 0;
            string name = string.Empty;

            await RunGuardedAsync(() => InTransactionAsync(async () =>
            {
                var (t, s) = await LoadTopicAsync(examId, id, ct);
                name = t.Name;
                if (Key(confirmName) != Key(t.Name))
                    throw Err(400, "CONFIRM_NAME_MISMATCH", $"To confirm, type the topic's name exactly: {t.Name}");

                var u = await UsageAsync(exam, TopicQuestionIds(t, s), ct);
                if (!unassign)
                {
                    if (u.Questions > 0)
                        throw Err(409, "TOPIC_IN_USE", "This topic is still in use, so it was not deleted. Archive it, or unassign its questions first.",
                            BuildDeletePreview("topic", t.Id, t.Name, t.IsActive, u));
                }
                else if (expectedQuestions.HasValue && expectedQuestions.Value != u.Questions)
                {
                    throw Err(409, "IMPACT_CHANGED",
                        $"The affected questions changed since you opened the preview (it was {expectedQuestions.Value:N0}, it is now {u.Questions:N0}). Nothing was changed -- review the updated preview and confirm again.",
                        new { expected = expectedQuestions.Value, actual = u.Questions });
                }

                if (unassign)
                {
                    // Questions keep their exam SUBJECT; only the topic is cleared.
                    unassigned = await UnassignTopicAsync(adminId, examId, s, TopicQuestionIds(t, s), ct);
                    var strays = await _db.QuestionBankExamClassifications.Where(c => c.ExamTopicId == id).ToListAsync(ct);
                    foreach (var c in strays) { c.ExamTopicId = null; c.UpdatedAt = DateTime.UtcNow; c.UpdatedByAdminId = adminId; }
                    await _db.SaveChangesAsync(ct);
                }

                _db.ExamTopics.Remove(t);
                Audit(adminId, "ExamTopic.Delete", t.Id,
                    $"[{exam.Name} > {s.Name}] Deleted topic '{t.Name}'" + (unassign ? $" after clearing the topic on {unassigned:N0} question(s)." : " (it had no mapped questions)."));
                await _db.SaveChangesAsync(ct);
                return true;
            }, ct));

            return new ExamTaxonomyResultDto
            {
                Message = unassign
                    ? $"Deleted topic '{name}' from {exam.Name}. {unassigned:N0} question(s) keep their subject and now have no topic in this exam (the questions are untouched)."
                    : $"Deleted topic '{name}' from {exam.Name}.",
                Affected = unassigned
            };
        }

        private Task<int> UnassignTopicAsync(Guid adminId, Guid examId, ExamSubject subject, IQueryable<Guid> ids, CancellationToken ct) =>
            UnassignAsync(adminId, examId, ids, subject.Id, ct);

        // ------------------------------------------------------------------------------
        // Questions: list / map / unmap
        // ------------------------------------------------------------------------------

        public async Task<ExamTaxonomyQuestionListDto> ListQuestionsAsync(Guid examId, ExamTaxonomyQuestionQueryDto query, CancellationToken ct)
        {
            await LoadExamAsync(examId, ct);
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);
            var q = ExamQuestions(examId);

            if (query.ExamTopicId.HasValue)
            {
                var (t, s) = await LoadTopicAsync(examId, query.ExamTopicId.Value, ct);
                var ids = TopicQuestionIds(t, s);
                q = q.Where(x => ids.Contains(x.Id));
            }
            else if (query.ExamSubjectId.HasValue)
            {
                var s = await LoadSubjectAsync(examId, query.ExamSubjectId.Value, ct);
                var ids = SubjectQuestionIds(s);
                q = q.Where(x => ids.Contains(x.Id));
            }
            else if (query.Unassigned)
            {
                var assigned = _db.QuestionBankExamClassifications
                    .Where(c => c.ExamId == examId && c.ExamSubjectId != null).Select(c => c.QuestionBankQuestionId);
                var baseIds = _db.ExamSubjects.Where(s => s.ExamId == examId && s.BaseSubjectId != null).Select(s => s.BaseSubjectId!.Value);
                q = q.Where(x => !assigned.Contains(x.Id)
                    && (_db.QuestionBankExamClassifications.Any(c => c.QuestionBankQuestionId == x.Id && c.ExamId == examId)
                        || !baseIds.Contains(x.SubjectId)));
            }

            var term = NormalizeName(query.Search);
            if (term.Length > 0) q = q.Where(x => x.QuestionText.Contains(term));

            var total = await q.CountAsync(ct);
            var pageRows = await q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new { x.Id, x.QuestionText, x.SubjectId, x.TopicId })
                .ToListAsync(ct);

            var pageIds = pageRows.Select(r => r.Id).ToList();
            var tax = await LoadTaxonomyAsync(examId, ct);
            var cls = await _db.QuestionBankExamClassifications
                .Where(c => c.ExamId == examId && pageIds.Contains(c.QuestionBankQuestionId)).ToDictionaryAsync(c => c.QuestionBankQuestionId, ct);
            var years = (await _db.QuestionBankExamMappings.Where(m => m.ExamId == examId && pageIds.Contains(m.QuestionBankQuestionId))
                .Select(m => new { m.QuestionBankQuestionId, m.Year }).ToListAsync(ct))
                .GroupBy(m => m.QuestionBankQuestionId).ToDictionary(g => g.Key, g => g.Select(x => x.Year).OrderByDescending(y => y).ToList());
            var gSub = await GlobalSubjectNamesAsync(pageRows.Select(r => (Guid?)r.SubjectId), ct);
            var gTop = await GlobalTopicNamesAsync(pageRows.Select(r => (Guid?)r.TopicId), ct);

            var items = pageRows.Select(r =>
            {
                cls.TryGetValue(r.Id, out var c);
                var (s, t) = tax.Resolve(c != null, c?.ExamSubjectId, c?.ExamTopicId, r.SubjectId, r.TopicId);
                return new ExamTaxonomyQuestionDto
                {
                    Id = r.Id,
                    QuestionText = r.QuestionText.Length > 300 ? r.QuestionText[..300] + "…" : r.QuestionText,
                    GlobalSubject = gSub.GetValueOrDefault(r.SubjectId) ?? string.Empty,
                    GlobalTopic = gTop.GetValueOrDefault(r.TopicId) ?? string.Empty,
                    ExamSubjectId = s,
                    ExamSubjectName = s != null ? tax.Subjects[s.Value].Name : null,
                    ExamTopicId = t,
                    ExamTopicName = t != null ? tax.Topics[t.Value].Name : null,
                    Explicit = c != null,
                    Years = years.GetValueOrDefault(r.Id) ?? new List<int>()
                };
            }).ToList();

            return new ExamTaxonomyQuestionListDto { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
        }

        public async Task<ExamTaxonomyResultDto> MapQuestionsAsync(Guid adminId, Guid examId, ExamTaxonomyMapRequestDto dto, CancellationToken ct)
        {
            var exam = await LoadExamAsync(examId, ct);
            var ids = (dto.QuestionIds ?? new()).Distinct().ToList();
            if (ids.Count == 0) throw Err(400, "NO_QUESTIONS", "Select at least one question.");
            if (ids.Count > MaxMapBatch) throw Err(400, "TOO_MANY_QUESTIONS", $"Map at most {MaxMapBatch:N0} questions at a time.");

            int changed = 0;
            string label = string.Empty;
            await RunGuardedAsync(() => InTransactionAsync(async () =>
            {
                var subject = await LoadSubjectAsync(examId, dto.ExamSubjectId, ct);
                if (!subject.IsActive) throw Err(400, "SUBJECT_ARCHIVED", $"'{subject.Name}' is archived. Restore it before mapping questions to it.");
                ExamTopic? topic = null;
                if (dto.ExamTopicId.HasValue)
                {
                    topic = await _db.ExamTopics.FirstOrDefaultAsync(t => t.Id == dto.ExamTopicId.Value, ct);
                    if (topic == null || topic.ExamSubjectId != subject.Id)
                        throw Err(400, "TOPIC_NOT_UNDER_SUBJECT", "That topic doesn't belong to the selected subject in this exam.");
                    if (!topic.IsActive) throw Err(400, "TOPIC_ARCHIVED", $"'{topic.Name}' is archived. Restore it before mapping questions to it.");
                }

                // Every question must already be tagged to THIS exam -- this feature classifies, it never adds a question to an exam.
                var inExam = await _db.QuestionBankExamMappings
                    .Where(m => m.ExamId == examId && ids.Contains(m.QuestionBankQuestionId))
                    .Select(m => m.QuestionBankQuestionId).Distinct().ToListAsync(ct);
                var missing = ids.Except(inExam).ToList();
                if (missing.Count > 0)
                    throw Err(400, "QUESTIONS_NOT_IN_EXAM", $"{missing.Count:N0} selected question(s) are not tagged to {exam.Name}, so nothing was changed.", new { questionIds = missing.Take(50) });

                var existing = await _db.QuestionBankExamClassifications
                    .Where(c => c.ExamId == examId && ids.Contains(c.QuestionBankQuestionId)).ToDictionaryAsync(c => c.QuestionBankQuestionId, ct);
                var now = DateTime.UtcNow;
                foreach (var qid in ids)
                {
                    if (!existing.TryGetValue(qid, out var row))
                    {
                        row = new QuestionBankExamClassification { QuestionBankQuestionId = qid, ExamId = examId, CreatedAt = now };
                        _db.QuestionBankExamClassifications.Add(row);
                    }
                    if (row.ExamSubjectId == subject.Id && row.ExamTopicId == topic?.Id) continue;
                    row.ExamSubjectId = subject.Id;
                    row.ExamTopicId = topic?.Id;
                    row.UpdatedAt = now;
                    row.UpdatedByAdminId = adminId;
                    changed++;
                }
                label = topic != null ? $"{subject.Name} > {topic.Name}" : subject.Name;
                Audit(adminId, "ExamTaxonomy.MapQuestions", examId, $"[{exam.Name}] Mapped {ids.Count:N0} question(s) to '{label}' ({changed:N0} changed).");
                await _db.SaveChangesAsync(ct);
                return true;
            }, ct));

            return new ExamTaxonomyResultDto { Message = $"{changed:N0} question(s) mapped to '{label}' in {exam.Name} only.", Affected = changed };
        }

        public async Task<ExamTaxonomyResultDto> UnmapQuestionsAsync(Guid adminId, Guid examId, ExamTaxonomyUnmapRequestDto dto, CancellationToken ct)
        {
            var exam = await LoadExamAsync(examId, ct);
            var ids = (dto.QuestionIds ?? new()).Distinct().ToList();
            if (ids.Count == 0) throw Err(400, "NO_QUESTIONS", "Select at least one question.");
            if (ids.Count > MaxMapBatch) throw Err(400, "TOO_MANY_QUESTIONS", $"Unassign at most {MaxMapBatch:N0} questions at a time.");
            int changed = 0;
            await RunGuardedAsync(() => InTransactionAsync(async () =>
            {
                var inExam = await _db.QuestionBankExamMappings
                    .Where(m => m.ExamId == examId && ids.Contains(m.QuestionBankQuestionId))
                    .Select(m => m.QuestionBankQuestionId).Distinct().ToListAsync(ct);
                var missing = ids.Except(inExam).ToList();
                if (missing.Count > 0)
                    throw Err(400, "QUESTIONS_NOT_IN_EXAM", $"{missing.Count:N0} selected question(s) are not tagged to {exam.Name}, so nothing was changed.");
                var existing = await _db.QuestionBankExamClassifications
                    .Where(c => c.ExamId == examId && ids.Contains(c.QuestionBankQuestionId)).ToDictionaryAsync(c => c.QuestionBankQuestionId, ct);
                var now = DateTime.UtcNow;
                foreach (var qid in ids)
                {
                    if (!existing.TryGetValue(qid, out var row))
                    {
                        row = new QuestionBankExamClassification { QuestionBankQuestionId = qid, ExamId = examId, CreatedAt = now };
                        _db.QuestionBankExamClassifications.Add(row);
                    }
                    else if (row.ExamSubjectId == null && row.ExamTopicId == null) continue;
                    row.ExamSubjectId = null;
                    row.ExamTopicId = null;
                    row.UpdatedAt = now;
                    row.UpdatedByAdminId = adminId;
                    changed++;
                }
                Audit(adminId, "ExamTaxonomy.UnmapQuestions", examId, $"[{exam.Name}] Unassigned {changed:N0} question(s) from their exam subject/topic.");
                await _db.SaveChangesAsync(ct);
                return true;
            }, ct));
            return new ExamTaxonomyResultDto { Message = $"{changed:N0} question(s) are now unassigned in {exam.Name}. The questions themselves are untouched.", Affected = changed };
        }

        public async Task<List<QuestionExamClassificationDto>> GetQuestionClassificationsAsync(Guid questionId, CancellationToken ct)
        {
            var q = await _db.QuestionBankQuestions.AsNoTracking().Where(x => x.Id == questionId)
                .Select(x => new { x.Id, x.SubjectId, x.TopicId }).FirstOrDefaultAsync(ct)
                ?? throw Err(404, "QUESTION_NOT_FOUND", "That question doesn't exist.");

            var maps = await _db.QuestionBankExamMappings.AsNoTracking().Where(m => m.QuestionBankQuestionId == questionId)
                .Select(m => new { m.ExamId, m.Year }).ToListAsync(ct);
            var result = new List<QuestionExamClassificationDto>();
            foreach (var g in maps.GroupBy(m => m.ExamId))
            {
                var exam = await _db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == g.Key, ct);
                if (exam == null) continue;
                var tax = await LoadTaxonomyAsync(g.Key, ct);
                var c = await _db.QuestionBankExamClassifications.AsNoTracking().FirstOrDefaultAsync(x => x.QuestionBankQuestionId == questionId && x.ExamId == g.Key, ct);
                var (s, t) = tax.Resolve(c != null, c?.ExamSubjectId, c?.ExamTopicId, q.SubjectId, q.TopicId);
                result.Add(new QuestionExamClassificationDto
                {
                    ExamId = g.Key, ExamName = exam.Name, Years = g.Select(x => x.Year).OrderByDescending(y => y).ToList(),
                    ExamSubjectId = s, ExamSubjectName = s != null ? tax.Subjects[s.Value].Name : null,
                    ExamTopicId = t, ExamTopicName = t != null ? tax.Topics[t.Value].Name : null, Explicit = c != null
                });
            }
            return result.OrderBy(r => r.ExamName).ToList();
        }

        // ------------------------------------------------------------------------------
        // Initialise an exam from the legacy (global) classification -- previewed + confirmed + idempotent
        // ------------------------------------------------------------------------------

        private sealed class InitPlan
        {
            public Exam Exam { get; set; } = null!;
            public Taxonomy Tax { get; set; } = null!;
            public int QuestionsInExam { get; set; }
            public int QuestionsToClassify { get; set; }
            public List<Guid> SubjectsToCreate { get; } = new();                // global subject ids
            public List<(Guid Subject, Guid Topic)> TopicsToCreate { get; } = new();
            public Dictionary<Guid, int> QuestionsBySubject { get; } = new();   // global subject -> count
            public Dictionary<Guid, string> SubjectNames { get; set; } = new();
            public Dictionary<Guid, string> TopicNames { get; set; } = new();
            public List<string> Conflicts { get; } = new();
            public List<Combo> Combos { get; set; } = new();
        }

        private async Task<InitPlan> PlanInitAsync(Guid examId, CancellationToken ct)
        {
            var plan = new InitPlan { Exam = await LoadExamAsync(examId, ct) };
            plan.Tax = await LoadTaxonomyAsync(examId, ct);
            plan.Combos = await CombosAsync(examId, _db.QuestionBankQuestions, ct);
            plan.QuestionsInExam = plan.Combos.Sum(c => c.Count);

            var pending = plan.Combos.Where(c => !c.Has).ToList();
            plan.QuestionsToClassify = pending.Sum(c => c.Count);

            plan.SubjectNames = await GlobalSubjectNamesAsync(pending.Select(c => (Guid?)c.SubjectId), ct);
            plan.TopicNames = await GlobalTopicNamesAsync(pending.Select(c => (Guid?)c.TopicId), ct);

            // active exam-subject names currently taken (by key), so we can spot collisions up front
            var activeSubjectKeys = plan.Tax.Subjects.Values.Where(s => s.IsActive).ToDictionary(s => Key(s.Name), s => s);

            foreach (var g in pending.GroupBy(c => c.SubjectId))
            {
                plan.QuestionsBySubject[g.Key] = g.Sum(c => c.Count);
                var gName = plan.SubjectNames.GetValueOrDefault(g.Key) ?? "(unnamed)";
                if (!plan.Tax.SubjectByBase.ContainsKey(g.Key))
                {
                    if (activeSubjectKeys.TryGetValue(Key(gName), out var clash) && clash.BaseSubjectId != null)
                        plan.Conflicts.Add($"Subject '{gName}' would collide with the exam subject '{clash.Name}' (linked to a different global subject). Rename one of them first.");
                    else if (!(activeSubjectKeys.TryGetValue(Key(gName), out var adopt) && adopt.BaseSubjectId == null))
                        plan.SubjectsToCreate.Add(g.Key);
                }

                foreach (var tg in g.GroupBy(c => c.TopicId))
                {
                    var tName = plan.TopicNames.GetValueOrDefault(tg.Key) ?? "(unnamed)";
                    plan.Tax.SubjectByBase.TryGetValue(g.Key, out var existingSubject);
                    if (existingSubject != null && plan.Tax.TopicByBase.ContainsKey((existingSubject.Id, tg.Key))) continue;
                    if (existingSubject != null)
                    {
                        var clash = plan.Tax.Topics.Values.FirstOrDefault(t => t.ExamSubjectId == existingSubject.Id && t.IsActive && Key(t.Name) == Key(tName));
                        if (clash != null && clash.BaseTopicId != null)
                        {
                            plan.Conflicts.Add($"Topic '{tName}' under '{existingSubject.Name}' would collide with the topic '{clash.Name}' (linked to a different global topic). Rename one of them first.");
                            continue;
                        }
                        if (clash != null) continue; // exam-only topic of the same name is adopted
                    }
                    plan.TopicsToCreate.Add((g.Key, tg.Key));
                }
            }
            return plan;
        }

        public async Task<ExamTaxonomyInitPreviewDto> PreviewInitializeAsync(Guid examId, CancellationToken ct)
        {
            var plan = await PlanInitAsync(examId, ct);
            var pending = plan.Combos.Where(c => !c.Has).ToList();
            var subjects = pending.GroupBy(c => c.SubjectId).Select(g => new ExamTaxonomyInitPreviewSubjectDto
            {
                Name = plan.SubjectNames.GetValueOrDefault(g.Key) ?? "(unnamed)",
                Exists = plan.Tax.SubjectByBase.ContainsKey(g.Key),
                Questions = g.Sum(c => c.Count),
                Topics = g.Select(c => plan.TopicNames.GetValueOrDefault(c.TopicId) ?? "(unnamed)").Distinct().OrderBy(n => n).ToList()
            }).OrderBy(s => s.Name).ToList();

            return new ExamTaxonomyInitPreviewDto
            {
                ExamId = examId,
                ExamName = plan.Exam.Name,
                AlreadyInitialized = plan.Tax.Subjects.Count > 0,
                QuestionsInExam = plan.QuestionsInExam,
                QuestionsToClassify = plan.QuestionsToClassify,
                SubjectsToCreate = plan.SubjectsToCreate.Count,
                TopicsToCreate = plan.TopicsToCreate.Count,
                Subjects = subjects,
                Conflicts = plan.Conflicts,
                CanRun = plan.Conflicts.Count == 0 && plan.QuestionsToClassify > 0
            };
        }

        public async Task<ExamTaxonomyResultDto> InitializeAsync(Guid adminId, Guid examId, ExamTaxonomyInitRequestDto dto, CancellationToken ct)
        {
            if (!dto.Confirm) throw Err(400, "CONFIRMATION_REQUIRED", "Please confirm this action before continuing.");
            int classified = 0, subjectsCreated = 0, topicsCreated = 0;
            string examName = string.Empty;

            await RunGuardedAsync(() => InTransactionAsync(async () =>
            {
                var plan = await PlanInitAsync(examId, ct);
                examName = plan.Exam.Name;
                if (plan.Conflicts.Count > 0)
                    throw Err(409, "INIT_NAME_CONFLICT", "Some names collide with existing subjects/topics of this exam. Nothing was changed -- resolve them and try again.", new { conflicts = plan.Conflicts });
                if (plan.QuestionsToClassify == 0)
                    throw Err(409, "NOTHING_TO_INITIALIZE", "Every question of this exam already has a classification.");
                if (dto.ExpectedQuestionsToClassify.HasValue && dto.ExpectedQuestionsToClassify.Value != plan.QuestionsToClassify)
                    throw Err(409, "IMPACT_CHANGED",
                        $"The number of questions to classify changed since the preview (was {dto.ExpectedQuestionsToClassify.Value:N0}, now {plan.QuestionsToClassify:N0}). Nothing was changed -- review the updated preview.",
                        new { expected = dto.ExpectedQuestionsToClassify.Value, actual = plan.QuestionsToClassify });

                var now = DateTime.UtcNow;
                var activeByKey = plan.Tax.Subjects.Values.Where(s => s.IsActive).ToDictionary(s => Key(s.Name));
                var subjectMap = new Dictionary<Guid, ExamSubject>(plan.Tax.SubjectByBase);

                foreach (var gid in plan.SubjectsToCreate.Concat(plan.QuestionsBySubject.Keys.Where(k => !plan.Tax.SubjectByBase.ContainsKey(k) && !plan.SubjectsToCreate.Contains(k))).Distinct())
                {
                    var gName = plan.SubjectNames.GetValueOrDefault(gid) ?? "(unnamed)";
                    if (activeByKey.TryGetValue(Key(gName), out var adopt) && adopt.BaseSubjectId == null)
                    {
                        // An exam-only subject the admin already created with the same name: link lineage, don't duplicate.
                        var tracked = await _db.ExamSubjects.FirstAsync(s => s.Id == adopt.Id, ct);
                        tracked.BaseSubjectId = gid;
                        tracked.UpdatedAt = now;
                        subjectMap[gid] = tracked;
                        continue;
                    }
                    var created = new ExamSubject { ExamId = examId, BaseSubjectId = gid, Name = gName, CreatedByAdminId = adminId, CreatedAt = now };
                    _db.ExamSubjects.Add(created);
                    subjectMap[gid] = created;
                    activeByKey[Key(gName)] = created;
                    subjectsCreated++;
                }
                await _db.SaveChangesAsync(ct);

                var topicMap = new Dictionary<(Guid Subject, Guid Topic), ExamTopic>();
                foreach (var (gs, gt) in plan.TopicsToCreate)
                {
                    var es = subjectMap[gs];
                    var tName = plan.TopicNames.GetValueOrDefault(gt) ?? "(unnamed)";
                    var sibling = await _db.ExamTopics.FirstOrDefaultAsync(t => t.ExamSubjectId == es.Id && t.IsActive && t.Name.ToLower() == tName.ToLower(), ct);
                    if (sibling != null && sibling.BaseTopicId == null)
                    {
                        sibling.BaseTopicId = gt;
                        sibling.UpdatedAt = now;
                        topicMap[(gs, gt)] = sibling;
                        continue;
                    }
                    var t = new ExamTopic { ExamSubjectId = es.Id, BaseTopicId = gt, Name = tName, CreatedByAdminId = adminId, CreatedAt = now };
                    _db.ExamTopics.Add(t);
                    topicMap[(gs, gt)] = t;
                    topicsCreated++;
                }
                await _db.SaveChangesAsync(ct);

                // Existing topics reachable through lineage (re-runs on a partly initialised exam).
                foreach (var kv in plan.Tax.TopicByBase)
                {
                    var gs = plan.Tax.Subjects[kv.Value.ExamSubjectId].BaseSubjectId;
                    if (gs != null) topicMap.TryAdd((gs.Value, kv.Key.BaseTopic), await _db.ExamTopics.FirstAsync(t => t.Id == kv.Value.Id, ct));
                }

                // Classification rows for every question tagged to the exam that has none yet.
                var todo = await ExamQuestions(examId)
                    .Where(q => !_db.QuestionBankExamClassifications.Any(c => c.QuestionBankQuestionId == q.Id && c.ExamId == examId))
                    .Select(q => new { q.Id, q.SubjectId, q.TopicId }).ToListAsync(ct);
                foreach (var chunk in todo.Chunk(2000))
                {
                    foreach (var q in chunk)
                    {
                        subjectMap.TryGetValue(q.SubjectId, out var es);
                        ExamTopic? et = null;
                        if (es != null) topicMap.TryGetValue((q.SubjectId, q.TopicId), out et);
                        _db.QuestionBankExamClassifications.Add(new QuestionBankExamClassification
                        {
                            QuestionBankQuestionId = q.Id, ExamId = examId, ExamSubjectId = es?.Id, ExamTopicId = et?.Id,
                            CreatedAt = now, UpdatedByAdminId = adminId
                        });
                        classified++;
                    }
                    await _db.SaveChangesAsync(ct);
                }

                Audit(adminId, "ExamTaxonomy.Initialize", examId,
                    $"[{examName}] Initialised from the global classification: {subjectsCreated:N0} subject(s), {topicsCreated:N0} topic(s) created, {classified:N0} question(s) classified.");
                await _db.SaveChangesAsync(ct);
                return true;
            }, ct));

            return new ExamTaxonomyResultDto
            {
                Message = $"{examName}: created {subjectsCreated:N0} subject(s) and {topicsCreated:N0} topic(s) and classified {classified:N0} question(s). The questions themselves were not changed.",
                Affected = classified
            };
        }

        // ------------------------------------------------------------------------------
        // Student-facing helpers
        // ------------------------------------------------------------------------------

        public Task<bool> IsInitializedAsync(Guid examId, CancellationToken ct = default) =>
            _db.ExamSubjects.AnyAsync(s => s.ExamId == examId, ct);

        public async Task<List<ExamSubjectFilterDto>> GetStudentSubjectsAsync(Guid examId, IQueryable<QuestionBankQuestion> visibleQuestions, CancellationToken ct = default)
        {
            var tax = await LoadTaxonomyAsync(examId, ct);
            var counts = await CountAsync(examId, visibleQuestions, tax, ct);
            return tax.Subjects.Values.Where(s => s.IsActive).OrderBy(s => s.Name)
                .Select(s => new ExamSubjectFilterDto { Id = s.Id, Name = s.Name, QuestionCount = counts.BySubject.GetValueOrDefault(s.Id) })
                .ToList();
        }

        public async Task<List<ExamTopicFilterDto>> GetStudentTopicsAsync(Guid examId, Guid? examSubjectId, IQueryable<QuestionBankQuestion> visibleQuestions, CancellationToken ct = default)
        {
            var tax = await LoadTaxonomyAsync(examId, ct);
            var counts = await CountAsync(examId, visibleQuestions, tax, ct);
            return tax.Topics.Values
                .Where(t => t.IsActive && tax.Subjects.TryGetValue(t.ExamSubjectId, out var s) && s.IsActive
                    && (examSubjectId == null || t.ExamSubjectId == examSubjectId))
                .OrderBy(t => t.Name)
                .Select(t => new ExamTopicFilterDto
                {
                    Id = t.Id, SubjectId = t.ExamSubjectId, SubjectName = tax.Subjects[t.ExamSubjectId].Name,
                    Name = t.Name, QuestionCount = counts.ByTopic.GetValueOrDefault(t.Id)
                }).ToList();
        }

        public IQueryable<QuestionBankQuestion> FilterByExamSubjects(IQueryable<QuestionBankQuestion> source, IReadOnlyCollection<Guid> examSubjectIds)
        {
            var list = examSubjectIds.ToList();
            return source.Where(x =>
                _db.QuestionBankExamClassifications.Any(c => c.QuestionBankQuestionId == x.Id && c.ExamSubjectId != null && list.Contains(c.ExamSubjectId.Value)
                    && _db.QuestionBankExamMappings.Any(m => m.QuestionBankQuestionId == x.Id && m.ExamId == c.ExamId))
                || _db.ExamSubjects.Any(es => list.Contains(es.Id) && es.BaseSubjectId == x.SubjectId
                    && _db.QuestionBankExamMappings.Any(m => m.QuestionBankQuestionId == x.Id && m.ExamId == es.ExamId)
                    && !_db.QuestionBankExamClassifications.Any(c => c.QuestionBankQuestionId == x.Id && c.ExamId == es.ExamId)));
        }

        public IQueryable<QuestionBankQuestion> FilterByExamTopics(IQueryable<QuestionBankQuestion> source, IReadOnlyCollection<Guid> examTopicIds)
        {
            var list = examTopicIds.ToList();
            return source.Where(x =>
                _db.QuestionBankExamClassifications.Any(c => c.QuestionBankQuestionId == x.Id && c.ExamTopicId != null && list.Contains(c.ExamTopicId.Value)
                    && _db.QuestionBankExamMappings.Any(m => m.QuestionBankQuestionId == x.Id && m.ExamId == c.ExamId))
                || _db.ExamTopics.Any(et => list.Contains(et.Id) && et.BaseTopicId == x.TopicId
                    && _db.ExamSubjects.Any(es => es.Id == et.ExamSubjectId && es.BaseSubjectId == x.SubjectId
                        && _db.QuestionBankExamMappings.Any(m => m.QuestionBankQuestionId == x.Id && m.ExamId == es.ExamId)
                        && !_db.QuestionBankExamClassifications.Any(c => c.QuestionBankQuestionId == x.Id && c.ExamId == es.ExamId))));
        }

        public async Task<Dictionary<Guid, (string? Subject, string? Topic)>> ResolveNamesAsync(Guid examId, IReadOnlyCollection<(Guid Id, Guid SubjectId, Guid TopicId)> questions, CancellationToken ct = default)
        {
            var result = new Dictionary<Guid, (string?, string?)>();
            if (questions.Count == 0) return result;
            var tax = await LoadTaxonomyAsync(examId, ct);
            if (tax.Subjects.Count == 0) return result;
            var ids = questions.Select(q => q.Id).ToList();
            var cls = await _db.QuestionBankExamClassifications.AsNoTracking()
                .Where(c => c.ExamId == examId && ids.Contains(c.QuestionBankQuestionId)).ToDictionaryAsync(c => c.QuestionBankQuestionId, ct);
            foreach (var q in questions)
            {
                cls.TryGetValue(q.Id, out var c);
                var (s, t) = tax.Resolve(c != null, c?.ExamSubjectId, c?.ExamTopicId, q.SubjectId, q.TopicId);
                if (s == null) continue; // unassigned in this exam: caller keeps the question's own (global) names
                result[q.Id] = (tax.Subjects[s.Value].Name, t != null ? tax.Topics[t.Value].Name : string.Empty);
            }
            return result;
        }

        // ------------------------------------------------------------------------------
        // Audit + transactions (same pattern as SubjectManagementService)
        // ------------------------------------------------------------------------------

        // Added to the SAME context/transaction as the change it describes: if the audit row can't be written the
        // change rolls back instead of happening un-audited.
        private void Audit(Guid adminId, string action, Guid targetId, string detail)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                AdminId = adminId,
                Action = action,
                TargetType = "ExamTaxonomy",
                TargetId = targetId,
                Detail = detail.Length <= 1000 ? detail : detail[..997] + "..."
            });
        }

        private async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear(); // a retry re-runs the whole delegate: start from a clean tracker
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    var result = await work();
                    await tx.CommitAsync(ct);
                    return result;
                }
                catch
                {
                    await tx.RollbackAsync(CancellationToken.None); // any failure => nothing from this operation is kept
                    throw;
                }
            });
        }

        private async Task RunGuardedAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException u && (u.Number == 2601 || u.Number == 2627))
            {
                throw Err(409, "DUPLICATE_NAME", "That would create a duplicate name. Please refresh and try again.");
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException f && f.Number == 547)
            {
                throw Err(409, "IN_USE", "This record is still referenced by other records, so the change couldn't be completed. Nothing was changed.");
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Exam taxonomy database update failed.");
                throw Err(500, "DATABASE_ERROR", "The database couldn't complete this change, so nothing was changed. Please try again.");
            }
        }
    }
}
