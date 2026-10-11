using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScoramAPI.Controllers;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Models;
using ScoramAPI.Services.Analytics;

namespace ScoramAPI.Services
{
    /// <summary>Stable reference to one question across both question sources ("qb:{guid}" or "q:{guid}").</summary>
    public readonly record struct QuestionKey(Guid? QuestionBankQuestionId, Guid? QuestionId)
    {
        public string Ref => QuestionBankQuestionId.HasValue
            ? "qb:" + QuestionBankQuestionId.Value.ToString()
            : "q:" + (QuestionId ?? Guid.Empty).ToString();

        public static bool TryParse(string? raw, out QuestionKey key)
        {
            key = default;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var s = raw.Trim();
            if (s.StartsWith("qb:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(s.AsSpan(3), out var qb) && qb != Guid.Empty)
            {
                key = new QuestionKey(qb, null);
                return true;
            }
            if (s.StartsWith("q:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(s.AsSpan(2), out var q) && q != Guid.Empty)
            {
                key = new QuestionKey(null, q);
                return true;
            }
            return false;
        }
    }

    /// <summary>Filters for the Mistake Book. Exam/kind/subject/topic/date/search narrow the underlying
    /// answers in SQL; Status/Type/DueOnly/Sort are applied afterwards on the per-question rows.</summary>
    public sealed class MistakeQuery
    {
        public IReadOnlyCollection<Guid>? ExamIds { get; set; }
        public TestKind? Kind { get; set; }
        public string? Subject { get; set; }
        public string? Topic { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string? Search { get; set; }
        /// <summary>Open (default: New+Learning) | New | Learning | Corrected | Dismissed | All</summary>
        public string Status { get; set; } = "Open";
        /// <summary>Incorrect (default) | Skipped | Repeated | Marked | All</summary>
        public string Type { get; set; } = "Incorrect";
        public bool DueOnly { get; set; }
        /// <summary>recent (default) | oldest | mostWrong</summary>
        public string Sort { get; set; } = "recent";
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    /// <summary>One question's mistake history merged with the student's revision state.</summary>
    public sealed class MistakeState
    {
        public QuestionKey Key { get; set; }
        public string? Subject { get; set; }
        public string? Topic { get; set; }
        public int Wrong { get; set; }
        public int Skipped { get; set; }
        public int Correct { get; set; }
        public bool Marked { get; set; }
        public DateTime LastMistakeAt { get; set; }
        public DateTime? LastCorrectAt { get; set; }
        public MistakeRevisionItem? Item { get; set; }
        public string Status { get; set; } = "New";
        public string? CorrectedBy { get; set; }
        public int EffectiveConsecutive { get; set; }
        public int RevisionWrong { get; set; }
        public bool IsRepeated { get; set; }
        public bool IsDue { get; set; }
        public bool IsOpen => Status == "New" || Status == "Learning";
    }

    public sealed record RevisionAnswerOutcome(RevisionAnswerResultDto? Result, int StatusCode, string? Error);

    public interface IRevisionService
    {
        Task<List<MistakeState>> GetMistakeStatesAsync(Guid userId, MistakeQuery query, CancellationToken ct);
        Task<RevisionSummaryDto> GetSummaryAsync(Guid userId, CancellationToken ct);
        Task<MistakeBookDto> GetMistakeBookAsync(Guid userId, MistakeQuery query, CancellationToken ct);
        Task<MistakeDetailDto?> GetMistakeDetailAsync(Guid userId, QuestionKey key, CancellationToken ct);
        Task<RevisionSessionDto> GetSessionAsync(Guid userId, MistakeQuery query, int count, CancellationToken ct);
        Task<RevisionAnswerOutcome> AnswerAsync(Guid userId, QuestionKey key, OptionLetter selected, int? timeSpentSeconds, Guid? clientRequestId, CancellationToken ct);
        Task<bool> SetDismissedAsync(Guid userId, QuestionKey key, bool dismissed, CancellationToken ct);
    }

    /// <summary>
    /// SMART PROGRESS -- Mistake Book + revision loop.
    ///
    /// The list of mistakes is DERIVED from the student's own StudentAnswers (nothing is copied into a
    /// parallel "mistakes" table); only revision STATE (answers given in the Mistake Book, dismissal,
    /// next-due date) is stored, in MistakeRevisionItem/RevisionAttempt. Revision answers never touch
    /// StudentTestResult/StudentAnswer, so an original assessment's score, rank and history can never
    /// be altered or inflated by revising.
    /// </summary>
    public class RevisionService : IRevisionService
    {
        private const int PreviewLength = 300;
        private readonly ScoramDbContext _db;
        private readonly AnalyticsOptions _o;

        public RevisionService(ScoramDbContext db, IOptions<AnalyticsOptions> options)
        {
            _db = db;
            _o = options.Value;
        }

        // ------------------------------------------------------------------ per-question mistake rows

        private sealed class MistakeRow
        {
            public Guid? QuestionBankQuestionId { get; set; }
            public Guid? QuestionId { get; set; }
            public string? Subject { get; set; }
            public string? Topic { get; set; }
            public int Wrong { get; set; }
            public int Skipped { get; set; }
            public int Correct { get; set; }
            public int MarkedCount { get; set; }
            public DateTime? LastMistakeAt { get; set; }
            public DateTime? LastCorrectAt { get; set; }
            public DateTime LastAnyAt { get; set; }
        }

        public async Task<List<MistakeState>> GetMistakeStatesAsync(Guid userId, MistakeQuery q, CancellationToken ct)
        {
            var answers = AttemptQueries.CompletedAnswers(_db, userId, q.Kind, q.ExamIds, q.From, q.To);

            if (!string.IsNullOrWhiteSpace(q.Subject))
            {
                var subject = q.Subject.Trim();
                answers = subject == Taxonomy.Unclassified
                    ? answers.Where(a => a.SubjectSnapshot == null || a.SubjectSnapshot == "")
                    : answers.Where(a => a.SubjectSnapshot == subject);
            }
            if (!string.IsNullOrWhiteSpace(q.Topic))
            {
                var topic = q.Topic.Trim();
                answers = topic == Taxonomy.Unclassified
                    ? answers.Where(a => a.TopicSnapshot == null || a.TopicSnapshot == "")
                    : answers.Where(a => a.TopicSnapshot == topic);
            }
            if (!string.IsNullOrWhiteSpace(q.Search))
            {
                var term = q.Search.Trim();
                answers = answers.Where(a => a.QuestionTextSnapshot.Contains(term));
            }

            var rows = await answers
                .GroupBy(a => new { a.QuestionBankQuestionId, a.QuestionId })
                .Select(g => new MistakeRow
                {
                    QuestionBankQuestionId = g.Key.QuestionBankQuestionId,
                    QuestionId = g.Key.QuestionId,
                    Subject = g.Max(a => a.SubjectSnapshot),
                    Topic = g.Max(a => a.TopicSnapshot),
                    Wrong = g.Count(a => a.SelectedOption != null && !a.IsCorrect),
                    Skipped = g.Count(a => a.SelectedOption == null),
                    Correct = g.Count(a => a.SelectedOption != null && a.IsCorrect),
                    MarkedCount = g.Count(a => a.IsMarkedForReview),
                    LastMistakeAt = g.Max(a => (a.SelectedOption == null || !a.IsCorrect)
                        ? (DateTime?)a.StudentTestResult!.AttemptedAt : null),
                    LastCorrectAt = g.Max(a => (a.SelectedOption != null && a.IsCorrect)
                        ? (DateTime?)a.StudentTestResult!.AttemptedAt : null),
                    LastAnyAt = g.Max(a => a.StudentTestResult!.AttemptedAt)
                })
                .ToListAsync(ct);

            // Rows that never had a mistake, a skip or a review flag are not part of the Mistake Book.
            rows = rows.Where(r => r.Wrong > 0 || r.Skipped > 0 || r.MarkedCount > 0).ToList();

            var items = await _db.MistakeRevisionItems.AsNoTracking().Where(i => i.UserId == userId).ToListAsync(ct);
            var itemByRef = items
                .GroupBy(i => new QuestionKey(i.QuestionBankQuestionId, i.QuestionId).Ref)
                .ToDictionary(g => g.Key, g => g.First());

            var now = DateTime.UtcNow;
            var states = new List<MistakeState>(rows.Count);
            foreach (var r in rows)
            {
                var key = new QuestionKey(r.QuestionBankQuestionId, r.QuestionId);
                itemByRef.TryGetValue(key.Ref, out var item);

                var lastMistake = r.LastMistakeAt ?? r.LastAnyAt;
                var derived = AnalyticsCalculator.DeriveMistakeStatus(
                    item?.IsDismissed ?? false, lastMistake, r.LastCorrectAt,
                    item?.ConsecutiveCorrect ?? 0, item?.LastRevisedAt, item?.CorrectedAt, _o);

                var revisionWrong = item == null ? 0 : Math.Max(0, item.RevisionAttemptCount - item.RevisionCorrectCount);
                var state = new MistakeState
                {
                    Key = key,
                    Subject = r.Subject,
                    Topic = r.Topic,
                    Wrong = r.Wrong,
                    Skipped = r.Skipped,
                    Correct = r.Correct,
                    Marked = r.MarkedCount > 0,
                    LastMistakeAt = lastMistake,
                    LastCorrectAt = r.LastCorrectAt,
                    Item = item,
                    Status = derived.Status,
                    CorrectedBy = derived.CorrectedBy,
                    EffectiveConsecutive = derived.EffectiveConsecutiveCorrect,
                    RevisionWrong = revisionWrong,
                    IsRepeated = r.Wrong + revisionWrong >= 2
                };
                state.IsDue = state.IsOpen && (item?.NextDueAt == null || item.NextDueAt <= now);
                states.Add(state);
            }
            return states;
        }

        private static RevisionSummaryDto Summarise(IEnumerable<MistakeState> states)
        {
            var s = new RevisionSummaryDto();
            foreach (var m in states.Where(x => x.Wrong > 0))
            {
                switch (m.Status)
                {
                    case "New": s.New++; s.Open++; if (m.IsRepeated) s.Repeated++; if (m.IsDue) s.DueNow++; break;
                    case "Learning": s.Learning++; s.Open++; if (m.IsRepeated) s.Repeated++; if (m.IsDue) s.DueNow++; break;
                    case "Corrected": s.Corrected++; break;
                    case "Dismissed": s.Dismissed++; break;
                }
            }
            return s;
        }

        public async Task<RevisionSummaryDto> GetSummaryAsync(Guid userId, CancellationToken ct)
        {
            var states = await GetMistakeStatesAsync(userId, new MistakeQuery(), ct);
            var summary = Summarise(states);
            summary.RevisionAnswersLast7Days = await _db.RevisionAttempts.AsNoTracking()
                .CountAsync(a => a.UserId == userId && a.AnsweredAt >= DateTime.UtcNow.AddDays(-7), ct);
            return summary;
        }

        // ------------------------------------------------------------------ Mistake Book list

        private sealed class LatestRow
        {
            public Guid AnswerId { get; set; }
            public Guid AttemptId { get; set; }
            public Guid? QuestionBankQuestionId { get; set; }
            public Guid? QuestionId { get; set; }
            public string QuestionText { get; set; } = string.Empty;
            public string? QuestionImageUrl { get; set; }
            public OptionLetter? Selected { get; set; }
            public TestKind Kind { get; set; }
            public DateTime At { get; set; }
        }

        public async Task<MistakeBookDto> GetMistakeBookAsync(Guid userId, MistakeQuery q, CancellationToken ct)
        {
            var page = Math.Max(1, q.Page);
            var pageSize = Math.Clamp(q.PageSize, 1, _o.MaxPageSize);

            var all = await GetMistakeStatesAsync(userId, q, ct);
            var dto = new MistakeBookDto { Page = page, PageSize = pageSize, Summary = Summarise(all) };
            dto.Summary.RevisionAnswersLast7Days = await _db.RevisionAttempts.AsNoTracking()
                .CountAsync(a => a.UserId == userId && a.AnsweredAt >= DateTime.UtcNow.AddDays(-7), ct);

            IEnumerable<MistakeState> filtered = all;

            filtered = (q.Status ?? "Open").ToLowerInvariant() switch
            {
                "all" => filtered,
                "new" => filtered.Where(s => s.Status == "New"),
                "learning" => filtered.Where(s => s.Status == "Learning"),
                "corrected" => filtered.Where(s => s.Status == "Corrected"),
                "dismissed" => filtered.Where(s => s.Status == "Dismissed"),
                _ => filtered.Where(s => s.IsOpen)
            };

            filtered = (q.Type ?? "Incorrect").ToLowerInvariant() switch
            {
                "all" => filtered,
                "skipped" => filtered.Where(s => s.Skipped > 0),
                "repeated" => filtered.Where(s => s.IsRepeated),
                "marked" => filtered.Where(s => s.Marked),
                _ => filtered.Where(s => s.Wrong > 0)
            };

            if (q.DueOnly) filtered = filtered.Where(s => s.IsDue);

            filtered = (q.Sort ?? "recent").ToLowerInvariant() switch
            {
                "oldest" => filtered.OrderBy(s => s.LastMistakeAt).ThenBy(s => s.Key.Ref, StringComparer.Ordinal),
                "mostwrong" => filtered.OrderByDescending(s => s.Wrong + s.RevisionWrong)
                    .ThenByDescending(s => s.LastMistakeAt).ThenBy(s => s.Key.Ref, StringComparer.Ordinal),
                _ => filtered.OrderByDescending(s => s.LastMistakeAt).ThenBy(s => s.Key.Ref, StringComparer.Ordinal)
            };

            var list = filtered.ToList();
            dto.TotalCount = list.Count;
            var pageStates = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            if (pageStates.Count == 0) return dto;

            var latest = await LoadLatestRowsAsync(userId, pageStates.Select(s => s.Key).ToList(), ct);
            foreach (var s in pageStates)
            {
                latest.TryGetValue(s.Key.Ref, out var l);
                dto.Items.Add(ToItemDto(s, l));
            }
            return dto;
        }

        /// <summary>Most recent completed answer per question (lean columns only), keyed by QuestionRef.</summary>
        private async Task<Dictionary<string, LatestRow>> LoadLatestRowsAsync(Guid userId, List<QuestionKey> keys, CancellationToken ct)
        {
            var qbIds = keys.Where(k => k.QuestionBankQuestionId.HasValue).Select(k => k.QuestionBankQuestionId!.Value).ToList();
            var qIds = keys.Where(k => k.QuestionId.HasValue).Select(k => k.QuestionId!.Value).ToList();

            var rows = await _db.StudentAnswers.AsNoTracking()
                .Where(a => a.StudentTestResult!.UserId == userId
                            && a.StudentTestResult.Status != TestAttemptStatus.InProgress
                            && ((a.QuestionBankQuestionId != null && qbIds.Contains(a.QuestionBankQuestionId.Value))
                                || (a.QuestionId != null && qIds.Contains(a.QuestionId.Value))))
                .OrderByDescending(a => a.StudentTestResult!.AttemptedAt)
                .Select(a => new LatestRow
                {
                    AnswerId = a.Id,
                    AttemptId = a.StudentTestResultId,
                    QuestionBankQuestionId = a.QuestionBankQuestionId,
                    QuestionId = a.QuestionId,
                    QuestionText = a.QuestionTextSnapshot,
                    QuestionImageUrl = a.QuestionImageUrlSnapshot,
                    Selected = a.SelectedOption,
                    Kind = a.StudentTestResult!.TestKind,
                    At = a.StudentTestResult.AttemptedAt
                })
                .ToListAsync(ct);

            var result = new Dictionary<string, LatestRow>(StringComparer.Ordinal);
            foreach (var r in rows)
            {
                var k = new QuestionKey(r.QuestionBankQuestionId, r.QuestionId).Ref;
                if (!result.ContainsKey(k)) result[k] = r; // rows are newest-first
            }
            return result;
        }

        private MistakeItemDto ToItemDto(MistakeState s, LatestRow? latest)
        {
            var item = s.Item;
            var preview = latest?.QuestionText ?? string.Empty;
            if (preview.Length > PreviewLength) preview = preview[..PreviewLength].TrimEnd() + "...";

            return new MistakeItemDto
            {
                QuestionRef = s.Key.Ref,
                Subject = Taxonomy.SubjectName(s.Subject),
                Topic = Taxonomy.TopicName(s.Topic),
                IsUnclassified = Taxonomy.IsUnclassified(s.Subject) || Taxonomy.IsUnclassified(s.Topic),
                QuestionPreview = preview,
                QuestionImageUrl = latest?.QuestionImageUrl,
                TimesWrong = s.Wrong,
                TimesSkipped = s.Skipped,
                TimesCorrect = s.Correct,
                IsRepeated = s.IsRepeated,
                IsMarkedForReview = s.Marked,
                LastMistakeAt = s.LastMistakeAt,
                LastAttemptId = latest?.AttemptId,
                LastTestKind = latest?.Kind.ToString(),
                LastSelectedOption = latest?.Selected?.ToString(),
                Status = s.Status,
                CorrectedBy = s.CorrectedBy,
                Revision = new MistakeRevisionStateDto
                {
                    Attempts = item?.RevisionAttemptCount ?? 0,
                    Correct = item?.RevisionCorrectCount ?? 0,
                    ConsecutiveCorrect = s.EffectiveConsecutive,
                    LastRevisedAt = item?.LastRevisedAt,
                    NextDueAt = item?.NextDueAt,
                    IsDue = s.IsDue
                }
            };
        }

        // ------------------------------------------------------------------ detail

        private static IQueryable<StudentAnswer> ForKey(IQueryable<StudentAnswer> q, QuestionKey key)
        {
            var qb = key.QuestionBankQuestionId;
            var legacy = key.QuestionId;
            return qb.HasValue
                ? q.Where(a => a.QuestionBankQuestionId == qb)
                : q.Where(a => a.QuestionId == legacy);
        }

        private IQueryable<MistakeRevisionItem> ItemsFor(Guid userId, QuestionKey key, bool tracking)
        {
            var qb = key.QuestionBankQuestionId;
            var legacy = key.QuestionId;
            IQueryable<MistakeRevisionItem> all = tracking ? _db.MistakeRevisionItems : _db.MistakeRevisionItems.AsNoTracking();
            return qb.HasValue
                ? all.Where(i => i.UserId == userId && i.QuestionBankQuestionId == qb)
                : all.Where(i => i.UserId == userId && i.QuestionId == legacy);
        }

        private IQueryable<RevisionAttempt> RevisionsFor(Guid userId, QuestionKey key)
        {
            var qb = key.QuestionBankQuestionId;
            var legacy = key.QuestionId;
            var all = _db.RevisionAttempts.AsNoTracking();
            return qb.HasValue
                ? all.Where(a => a.UserId == userId && a.QuestionBankQuestionId == qb)
                : all.Where(a => a.UserId == userId && a.QuestionId == legacy);
        }

        public async Task<MistakeDetailDto?> GetMistakeDetailAsync(Guid userId, QuestionKey key, CancellationToken ct)
        {
            var history = await ForKey(AttemptQueries.CompletedAnswers(_db, userId, null, null, null, null), key)
                .OrderByDescending(a => a.StudentTestResult!.AttemptedAt)
                .Select(a => new
                {
                    a.Id,
                    AttemptId = a.StudentTestResultId,
                    a.SelectedOption,
                    a.IsCorrect,
                    At = a.StudentTestResult!.AttemptedAt
                })
                .ToListAsync(ct);
            if (history.Count == 0) return null;

            var latestFull = await _db.StudentAnswers.AsNoTracking().FirstAsync(a => a.Id == history[0].Id, ct);

            var attemptIds = history.Select(h => h.AttemptId).Distinct().ToList();
            var attempts = await _db.StudentTestResults.AsNoTracking()
                .Include(r => r.MockTest)
                .Include(r => r.PracticeTestTemplate)
                .Include(r => r.Paper).ThenInclude(p => p!.Exam)
                .Include(r => r.Quiz)
                .Where(r => attemptIds.Contains(r.Id))
                .ToListAsync(ct);
            var attemptById = attempts.ToDictionary(a => a.Id);

            // The item DTO reuses the same state derivation as the list so the two can never disagree.
            var states = await GetMistakeStatesAsync(userId, new MistakeQuery(), ct);
            var state = states.FirstOrDefault(s => s.Key.Ref == key.Ref);
            if (state == null) return null;

            var latestRow = new LatestRow
            {
                AnswerId = latestFull.Id,
                AttemptId = latestFull.StudentTestResultId,
                QuestionText = latestFull.QuestionTextSnapshot,
                QuestionImageUrl = latestFull.QuestionImageUrlSnapshot,
                Selected = latestFull.SelectedOption,
                Kind = attemptById.TryGetValue(latestFull.StudentTestResultId, out var la) ? la.TestKind : TestKind.Practice,
                At = history[0].At
            };

            var detail = new MistakeDetailDto
            {
                Item = ToItemDto(state, latestRow),
                QuestionText = latestFull.QuestionTextSnapshot,
                OptionA = latestFull.OptionASnapshot,
                OptionB = latestFull.OptionBSnapshot,
                OptionC = latestFull.OptionCSnapshot,
                OptionD = latestFull.OptionDSnapshot,
                CorrectOption = latestFull.CorrectOptionSnapshot.ToString(),
                QuestionImageUrl = latestFull.QuestionImageUrlSnapshot,
                OptionAImageUrl = latestFull.OptionAImageUrlSnapshot,
                OptionBImageUrl = latestFull.OptionBImageUrlSnapshot,
                OptionCImageUrl = latestFull.OptionCImageUrlSnapshot,
                OptionDImageUrl = latestFull.OptionDImageUrlSnapshot,
                ExplanationImageUrl = latestFull.ExplanationImageUrlSnapshot,
                ContentBlocks = ContentBlocksJsonHelper.Parse(latestFull.ContentBlocksJsonSnapshot),
                Explanation = latestFull.ExplanationSnapshot,
                SourceQuestionId = latestFull.QuestionId,
                SourceQuestionBankQuestionId = latestFull.QuestionBankQuestionId,
                IsQuestionBank = latestFull.QuestionBankQuestionId != null
            };

            foreach (var h in history)
            {
                attemptById.TryGetValue(h.AttemptId, out var attempt);
                detail.History.Add(new MistakeHistoryEntryDto
                {
                    AttemptId = h.AttemptId,
                    TestKind = attempt?.TestKind.ToString() ?? string.Empty,
                    Title = attempt != null ? TestAttemptsController.TitleFor(attempt) : "Assessment",
                    AttemptedAt = h.At,
                    SelectedOption = h.SelectedOption?.ToString(),
                    IsCorrect = h.IsCorrect,
                    WasSkipped = h.SelectedOption == null
                });
            }

            var revisions = await RevisionsFor(userId, key)
                .OrderByDescending(a => a.AnsweredAt)
                .Take(30)
                .Select(a => new RevisionHistoryEntryDto
                {
                    AnsweredAt = a.AnsweredAt,
                    SelectedOption = a.SelectedOption.ToString(),
                    IsCorrect = a.IsCorrect
                })
                .ToListAsync(ct);
            detail.RevisionHistory = revisions;
            return detail;
        }

        // ------------------------------------------------------------------ revision session

        public async Task<RevisionSessionDto> GetSessionAsync(Guid userId, MistakeQuery q, int count, CancellationToken ct)
        {
            count = Math.Clamp(count, 1, _o.MaxSessionQuestions);
            var states = await GetMistakeStatesAsync(userId, q, ct);

            var eligible = states.Where(s => s.Wrong > 0 && s.IsOpen);
            if (q.DueOnly) eligible = eligible.Where(s => s.IsDue);
            var ordered = eligible
                .OrderByDescending(s => s.IsDue)
                .ThenByDescending(s => s.IsRepeated)
                .ThenBy(s => s.LastMistakeAt)
                .ThenBy(s => s.Key.Ref, StringComparer.Ordinal)
                .ToList();

            var session = new RevisionSessionDto { MatchingOpenCount = ordered.Count };
            var chosen = ordered.Take(count).ToList();
            if (chosen.Count == 0) return session;

            var keys = chosen.Select(s => s.Key).ToList();
            var qbIds = keys.Where(k => k.QuestionBankQuestionId.HasValue).Select(k => k.QuestionBankQuestionId!.Value).ToList();
            var qIds = keys.Where(k => k.QuestionId.HasValue).Select(k => k.QuestionId!.Value).ToList();

            // Newest snapshot per question; the answer key columns are deliberately NOT selected.
            var rows = await _db.StudentAnswers.AsNoTracking()
                .Where(a => a.StudentTestResult!.UserId == userId
                            && a.StudentTestResult.Status != TestAttemptStatus.InProgress
                            && ((a.QuestionBankQuestionId != null && qbIds.Contains(a.QuestionBankQuestionId.Value))
                                || (a.QuestionId != null && qIds.Contains(a.QuestionId.Value))))
                .OrderByDescending(a => a.StudentTestResult!.AttemptedAt)
                .Select(a => new
                {
                    a.QuestionBankQuestionId,
                    a.QuestionId,
                    a.SubjectSnapshot,
                    a.TopicSnapshot,
                    a.QuestionTextSnapshot,
                    a.OptionASnapshot,
                    a.OptionBSnapshot,
                    a.OptionCSnapshot,
                    a.OptionDSnapshot,
                    a.QuestionImageUrlSnapshot,
                    a.OptionAImageUrlSnapshot,
                    a.OptionBImageUrlSnapshot,
                    a.OptionCImageUrlSnapshot,
                    a.OptionDImageUrlSnapshot,
                    a.ContentBlocksJsonSnapshot
                })
                .ToListAsync(ct);

            var newest = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < rows.Count; i++)
            {
                var k = new QuestionKey(rows[i].QuestionBankQuestionId, rows[i].QuestionId).Ref;
                if (!newest.ContainsKey(k)) newest[k] = i;
            }

            foreach (var s in chosen)
            {
                if (!newest.TryGetValue(s.Key.Ref, out var idx)) continue;
                var r = rows[idx];
                session.Questions.Add(new RevisionQuestionDto
                {
                    QuestionRef = s.Key.Ref,
                    Subject = Taxonomy.SubjectName(r.SubjectSnapshot),
                    Topic = Taxonomy.TopicName(r.TopicSnapshot),
                    QuestionText = r.QuestionTextSnapshot,
                    OptionA = r.OptionASnapshot,
                    OptionB = r.OptionBSnapshot,
                    OptionC = r.OptionCSnapshot,
                    OptionD = r.OptionDSnapshot,
                    QuestionImageUrl = r.QuestionImageUrlSnapshot,
                    OptionAImageUrl = r.OptionAImageUrlSnapshot,
                    OptionBImageUrl = r.OptionBImageUrlSnapshot,
                    OptionCImageUrl = r.OptionCImageUrlSnapshot,
                    OptionDImageUrl = r.OptionDImageUrlSnapshot,
                    ContentBlocks = ContentBlocksJsonHelper.Parse(r.ContentBlocksJsonSnapshot),
                    TimesWrong = s.Wrong + s.RevisionWrong,
                    IsRepeated = s.IsRepeated
                });
            }
            return session;
        }

        // ------------------------------------------------------------------ answering

        public async Task<RevisionAnswerOutcome> AnswerAsync(
            Guid userId, QuestionKey key, OptionLetter selected, int? timeSpentSeconds, Guid? clientRequestId, CancellationToken ct)
        {
            // Retried request (timeout / double tap): hand back the first outcome, record nothing new.
            if (clientRequestId.HasValue)
            {
                var duplicate = await BuildDuplicateOutcomeAsync(userId, key, clientRequestId.Value, ct);
                if (duplicate != null) return duplicate;
            }

            // The student can only revise a question that is in their own completed history.
            var history = ForKey(AttemptQueries.CompletedAnswers(_db, userId, null, null, null, null), key);
            var latest = await history
                .OrderByDescending(a => a.StudentTestResult!.AttemptedAt)
                .Select(a => new
                {
                    a.SubjectSnapshot,
                    a.TopicSnapshot,
                    a.CorrectOptionSnapshot,
                    a.ExplanationSnapshot,
                    a.ExplanationImageUrlSnapshot
                })
                .FirstOrDefaultAsync(ct);
            if (latest == null) return new RevisionAnswerOutcome(null, 404, "That question isn't in your history.");

            var marks = await history
                .GroupBy(a => 1)
                .Select(g => new
                {
                    LastMistakeAt = g.Max(a => (a.SelectedOption == null || !a.IsCorrect)
                        ? (DateTime?)a.StudentTestResult!.AttemptedAt : null),
                    LastCorrectAt = g.Max(a => (a.SelectedOption != null && a.IsCorrect)
                        ? (DateTime?)a.StudentTestResult!.AttemptedAt : null),
                    LastAnyAt = g.Max(a => a.StudentTestResult!.AttemptedAt)
                })
                .FirstAsync(ct);
            var lastMistake = marks.LastMistakeAt ?? marks.LastAnyAt;

            var qb = key.QuestionBankQuestionId;
            var legacy = key.QuestionId;
            var item = await ItemsFor(userId, key, tracking: true).FirstOrDefaultAsync(ct);

            var now = DateTime.UtcNow;
            if (item == null)
            {
                item = new MistakeRevisionItem
                {
                    UserId = userId,
                    QuestionBankQuestionId = qb,
                    QuestionId = qb.HasValue ? null : legacy,
                    CreatedAt = now
                };
                _db.MistakeRevisionItems.Add(item);
            }

            var revisedSinceMistake = item.LastRevisedAt.HasValue && item.LastRevisedAt.Value >= lastMistake;
            var baseStreak = revisedSinceMistake ? item.ConsecutiveCorrect : 0;
            var isCorrect = selected == latest.CorrectOptionSnapshot;
            var newStreak = isCorrect ? baseStreak + 1 : 0;
            var wasCorrectedBefore = AnalyticsCalculator.DeriveMistakeStatus(
                item.IsDismissed, lastMistake, marks.LastCorrectAt, item.ConsecutiveCorrect,
                item.LastRevisedAt, item.CorrectedAt, _o).Status == "Corrected";

            item.RevisionAttemptCount++;
            if (isCorrect) item.RevisionCorrectCount++;
            item.ConsecutiveCorrect = newStreak;
            item.LastRevisedAt = now;
            item.NextDueAt = AnalyticsCalculator.NextDueAt(now, isCorrect, newStreak, _o);
            item.UpdatedAt = now;

            var correctedNow = false;
            if (isCorrect && newStreak >= _o.CorrectedAfterConsecutiveCorrect)
            {
                if (!wasCorrectedBefore) correctedNow = true;
                item.CorrectedAt = now;
            }
            else if (!isCorrect)
            {
                item.CorrectedAt = null;
            }

            _db.RevisionAttempts.Add(new RevisionAttempt
            {
                UserId = userId,
                QuestionBankQuestionId = qb,
                QuestionId = qb.HasValue ? null : legacy,
                SelectedOption = selected,
                IsCorrect = isCorrect,
                TimeSpentSeconds = timeSpentSeconds.HasValue ? Math.Clamp(timeSpentSeconds.Value, 0, _o.MaxSecondsPerQuestion) : null,
                SubjectSnapshot = Taxonomy.Clean(latest.SubjectSnapshot),
                TopicSnapshot = Taxonomy.Clean(latest.TopicSnapshot),
                AnsweredAt = now,
                ClientRequestId = clientRequestId
            });

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A concurrent request lost a unique-index race: either the same ClientRequestId (a retry
                // overlapping its original) or the first-ever revision row for this question.
                _db.ChangeTracker.Clear();
                if (clientRequestId.HasValue)
                {
                    var duplicate = await BuildDuplicateOutcomeAsync(userId, key, clientRequestId.Value, ct);
                    if (duplicate != null) return duplicate;
                }
                return new RevisionAnswerOutcome(null, 409, "Couldn't record that answer just now - please try again.");
            }

            var status = AnalyticsCalculator.DeriveMistakeStatus(
                false, lastMistake, marks.LastCorrectAt, item.ConsecutiveCorrect,
                item.LastRevisedAt, item.CorrectedAt, _o).Status;

            return new RevisionAnswerOutcome(new RevisionAnswerResultDto
            {
                IsCorrect = isCorrect,
                CorrectOption = latest.CorrectOptionSnapshot.ToString(),
                Explanation = latest.ExplanationSnapshot,
                ExplanationImageUrl = latest.ExplanationImageUrlSnapshot,
                Status = status,
                ConsecutiveCorrect = newStreak,
                CorrectionTarget = _o.CorrectedAfterConsecutiveCorrect,
                CorrectedNow = correctedNow,
                NextDueAt = item.NextDueAt,
                WasDuplicate = false
            }, 200, null);
        }

        private async Task<RevisionAnswerOutcome?> BuildDuplicateOutcomeAsync(Guid userId, QuestionKey key, Guid clientRequestId, CancellationToken ct)
        {
            var existing = await _db.RevisionAttempts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.UserId == userId && a.ClientRequestId == clientRequestId, ct);
            if (existing == null) return null;

            var sameQuestion = key.QuestionBankQuestionId.HasValue
                ? existing.QuestionBankQuestionId == key.QuestionBankQuestionId
                : existing.QuestionId == key.QuestionId;
            if (!sameQuestion) return new RevisionAnswerOutcome(null, 409, "That request id was already used for a different question.");

            var latest = await ForKey(AttemptQueries.CompletedAnswers(_db, userId, null, null, null, null), key)
                .OrderByDescending(a => a.StudentTestResult!.AttemptedAt)
                .Select(a => new { a.CorrectOptionSnapshot, a.ExplanationSnapshot, a.ExplanationImageUrlSnapshot })
                .FirstOrDefaultAsync(ct);
            var item = await ItemsFor(userId, key, tracking: false).FirstOrDefaultAsync(ct);

            return new RevisionAnswerOutcome(new RevisionAnswerResultDto
            {
                IsCorrect = existing.IsCorrect,
                CorrectOption = latest?.CorrectOptionSnapshot.ToString() ?? string.Empty,
                Explanation = latest?.ExplanationSnapshot,
                ExplanationImageUrl = latest?.ExplanationImageUrlSnapshot,
                Status = item == null ? "Learning" : (item.ConsecutiveCorrect >= _o.CorrectedAfterConsecutiveCorrect ? "Corrected" : "Learning"),
                ConsecutiveCorrect = item?.ConsecutiveCorrect ?? 0,
                CorrectionTarget = _o.CorrectedAfterConsecutiveCorrect,
                CorrectedNow = false,
                NextDueAt = item?.NextDueAt,
                WasDuplicate = true
            }, 200, null);
        }

        // ------------------------------------------------------------------ dismiss / restore

        public async Task<bool> SetDismissedAsync(Guid userId, QuestionKey key, bool dismissed, CancellationToken ct)
        {
            var inHistory = await ForKey(AttemptQueries.CompletedAnswers(_db, userId, null, null, null, null), key).AnyAsync(ct);
            if (!inHistory) return false;

            var qb = key.QuestionBankQuestionId;
            var legacy = key.QuestionId;
            var item = await ItemsFor(userId, key, tracking: true).FirstOrDefaultAsync(ct);
            if (item == null)
            {
                if (!dismissed) return true; // nothing to restore
                item = new MistakeRevisionItem
                {
                    UserId = userId,
                    QuestionBankQuestionId = qb,
                    QuestionId = qb.HasValue ? null : legacy
                };
                _db.MistakeRevisionItems.Add(item);
            }
            item.IsDismissed = dismissed;
            item.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }
    }
}
