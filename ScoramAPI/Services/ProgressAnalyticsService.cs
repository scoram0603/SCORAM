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
    /// <summary>The narrowing options every progress endpoint shares. ExamIds is the RESOLVED exam set
    /// (an explicit examId and/or the student's My Exams); null = no exam narrowing. Quizzes have no
    /// exam, so any exam narrowing excludes them -- same as the existing Progress/Study Partner code.</summary>
    public sealed class ProgressFilter
    {
        public IReadOnlyCollection<Guid>? ExamIds { get; set; }
        public Guid? ExamId { get; set; }
        public TestKind? Kind { get; set; }
        public int? Days { get; set; }
        public bool MyExamsOnly { get; set; }

        public bool HasExamOrKindFilter => ExamIds != null || Kind.HasValue;
    }

    public sealed record TimingsOutcome(TestTimingsResponseDto? Result, int StatusCode, string? Error);

    public interface IProgressAnalyticsService
    {
        Task<ProgressOverviewDto> GetOverviewAsync(Guid userId, ProgressFilter filter, CancellationToken ct);
        Task<SubjectsResponseDto> GetSubjectsAsync(Guid userId, ProgressFilter filter, bool includeTopics, CancellationToken ct);
        Task<List<TopicInsightDto>> GetTopicInsightsAsync(Guid userId, ProgressFilter filter, CancellationToken ct);
        Task<TopicDetailDto?> GetTopicDetailAsync(Guid userId, ProgressFilter filter, string subject, string topic, CancellationToken ct);
        Task<TrendsDto> GetTrendsAsync(Guid userId, ProgressFilter filter, CancellationToken ct);
        Task<AttemptAnalyticsDto?> GetAttemptAnalyticsAsync(Guid userId, Guid attemptId, CancellationToken ct);
        Task<TimingsOutcome> RecordTimingsAsync(Guid userId, Guid attemptId, TestTimingsDto dto, CancellationToken ct);
    }

    /// <summary>
    /// SMART PROGRESS -- orchestrates the analytics queries and hands every number/label decision to
    /// AnalyticsCalculator. Aggregation happens in SQL (grouped queries over lean projections); only the
    /// small per-topic / per-attempt result sets are processed in memory.
    /// </summary>
    public class ProgressAnalyticsService : IProgressAnalyticsService
    {
        private readonly ScoramDbContext _db;
        private readonly IRevisionService _revision;
        private readonly AnalyticsOptions _o;

        public ProgressAnalyticsService(ScoramDbContext db, IRevisionService revision, IOptions<AnalyticsOptions> options)
        {
            _db = db;
            _revision = revision;
            _o = options.Value;
        }

        // ------------------------------------------------------------------ aggregation primitives

        private sealed class Agg
        {
            public string? Subject { get; set; }
            public string? Topic { get; set; }
            public int Total { get; set; }
            public int Attempted { get; set; }
            public int Correct { get; set; }
            public int Timed { get; set; }
            public long TimeSecs { get; set; }
            public DateTime? Last { get; set; }
        }

        private sealed class Bucket
        {
            public string Subject { get; set; } = Taxonomy.Unclassified;
            public string Topic { get; set; } = Taxonomy.Unclassified;
            public bool SubjectBlank { get; set; }
            public bool TopicBlank { get; set; }
            public Agg All { get; } = new();
            public Agg Recent { get; } = new();
            public Agg Previous { get; } = new();
        }

        private enum Slot { All, Recent, Previous }

        private async Task<List<Agg>> LoadAggsAsync(Guid userId, TestKind? kind, IReadOnlyCollection<Guid>? examIds,
            DateTime? from, DateTime? to, CancellationToken ct)
        {
            return await AttemptQueries.CompletedAnswers(_db, userId, kind, examIds, from, to)
                .GroupBy(a => new { a.SubjectSnapshot, a.TopicSnapshot })
                .Select(g => new Agg
                {
                    Subject = g.Key.SubjectSnapshot,
                    Topic = g.Key.TopicSnapshot,
                    Total = g.Count(),
                    Attempted = g.Count(a => a.SelectedOption != null),
                    Correct = g.Count(a => a.SelectedOption != null && a.IsCorrect),
                    Timed = g.Count(a => a.SelectedOption != null && a.TimeSpentSeconds != null),
                    TimeSecs = g.Sum(a => a.SelectedOption != null && a.TimeSpentSeconds != null ? (long)a.TimeSpentSeconds.Value : 0L),
                    Last = g.Max(a => (DateTime?)a.StudentTestResult!.AttemptedAt)
                })
                .ToListAsync(ct);
        }

        private static void Merge(Dictionary<string, Bucket> buckets, List<Agg> rows, Slot slot)
        {
            foreach (var r in rows)
            {
                var key = Taxonomy.TopicKey(r.Subject, r.Topic);
                if (!buckets.TryGetValue(key, out var b))
                {
                    b = new Bucket
                    {
                        Subject = Taxonomy.SubjectName(r.Subject),
                        Topic = Taxonomy.TopicName(r.Topic),
                        SubjectBlank = Taxonomy.IsUnclassified(r.Subject),
                        TopicBlank = Taxonomy.IsUnclassified(r.Topic)
                    };
                    buckets[key] = b;
                }
                var target = slot == Slot.All ? b.All : slot == Slot.Recent ? b.Recent : b.Previous;
                target.Total += r.Total;
                target.Attempted += r.Attempted;
                target.Correct += r.Correct;
                target.Timed += r.Timed;
                target.TimeSecs += r.TimeSecs;
                if (r.Last.HasValue && (!target.Last.HasValue || r.Last > target.Last)) target.Last = r.Last;
            }
        }

        private (DateTime? From, DateTime? To) Range(ProgressFilter f)
        {
            DateTime? from = f.Days.HasValue ? DateTime.UtcNow.AddDays(-Math.Clamp(f.Days.Value, 1, _o.MaxHistoryDays)) : null;
            return (from, null);
        }

        private async Task<Dictionary<string, Bucket>> BuildBucketsAsync(Guid userId, ProgressFilter f, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var win = Math.Max(1, _o.ComparisonWindowDays);
            var recentFrom = now.AddDays(-win);
            var prevFrom = now.AddDays(-2 * win);
            var (from, to) = Range(f);

            // Sequential on purpose: one DbContext cannot run concurrent queries.
            var all = await LoadAggsAsync(userId, f.Kind, f.ExamIds, from, to, ct);
            var recent = await LoadAggsAsync(userId, f.Kind, f.ExamIds, recentFrom, null, ct);
            var previous = await LoadAggsAsync(userId, f.Kind, f.ExamIds, prevFrom, recentFrom, ct);

            var buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);
            Merge(buckets, all, Slot.All);
            Merge(buckets, recent, Slot.Recent);
            Merge(buckets, previous, Slot.Previous);
            // A bucket that exists only in a window (outside the Days filter) has no "all" answers: drop it.
            return buckets.Where(kv => kv.Value.All.Total > 0).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        }

        private static WindowStats ToWindow(Agg a) => new(a.Attempted, a.Correct);

        private static WindowStatsDto ToWindowDto(WindowStats w, string? label) => new()
        {
            Attempted = w.Attempted,
            Correct = w.Correct,
            AccuracyPercent = w.Accuracy,
            Label = label
        };

        private ChangeDto BuildChange(WindowStats current, WindowStats previous, string currentLabel, string previousLabel)
        {
            var result = AnalyticsCalculator.Compare(current, previous, _o);
            return new ChangeDto
            {
                Status = result.Status.ToString(),
                Current = ToWindowDto(current, currentLabel),
                Previous = ToWindowDto(previous, previousLabel),
                AccuracyChangePoints = result.ChangePoints,
                Note = result.Note
            };
        }

        private ChangeDto BuildWindowChange(Bucket b)
        {
            var w = Math.Max(1, _o.ComparisonWindowDays);
            return BuildChange(ToWindow(b.Recent), ToWindow(b.Previous), $"Last {w} days", $"Previous {w} days");
        }

        private async Task<WindowStats> WindowAsync(Guid userId, TestKind? kind, IReadOnlyCollection<Guid>? examIds,
            DateTime? from, DateTime? to, CancellationToken ct)
        {
            var row = await AttemptQueries.CompletedAnswers(_db, userId, kind, examIds, from, to)
                .GroupBy(a => 1)
                .Select(g => new
                {
                    Attempted = g.Count(a => a.SelectedOption != null),
                    Correct = g.Count(a => a.SelectedOption != null && a.IsCorrect)
                })
                .FirstOrDefaultAsync(ct);
            return row == null ? new WindowStats(0, 0) : new WindowStats(row.Attempted, row.Correct);
        }

        private async Task<(ChangeDto Last7, ChangeDto Last30)> LoadRollingChangesAsync(Guid userId, TestKind? kind,
            IReadOnlyCollection<Guid>? examIds, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var c7 = await WindowAsync(userId, kind, examIds, now.AddDays(-7), null, ct);
            var p7 = await WindowAsync(userId, kind, examIds, now.AddDays(-14), now.AddDays(-7), ct);
            var c30 = await WindowAsync(userId, kind, examIds, now.AddDays(-30), null, ct);
            var p30 = await WindowAsync(userId, kind, examIds, now.AddDays(-60), now.AddDays(-30), ct);
            return (BuildChange(c7, p7, "Last 7 days", "Previous 7 days"),
                    BuildChange(c30, p30, "Last 30 days", "Previous 30 days"));
        }

        // ------------------------------------------------------------------ topic / subject insights

        private sealed class MistakeCounts
        {
            public int Open { get; set; }
            public int Corrected { get; set; }
            public int Repeated { get; set; }
        }

        private async Task<Dictionary<string, MistakeCounts>> LoadMistakeCountsAsync(Guid userId, ProgressFilter f, CancellationToken ct)
        {
            var (from, to) = Range(f);
            var states = await _revision.GetMistakeStatesAsync(userId, new MistakeQuery
            {
                ExamIds = f.ExamIds,
                Kind = f.Kind,
                From = from,
                To = to
            }, ct);

            var map = new Dictionary<string, MistakeCounts>(StringComparer.Ordinal);
            foreach (var s in states.Where(x => x.Wrong > 0))
            {
                var key = Taxonomy.TopicKey(s.Subject, s.Topic);
                if (!map.TryGetValue(key, out var c)) map[key] = c = new MistakeCounts();
                if (s.IsOpen)
                {
                    c.Open++;
                    if (s.IsRepeated) c.Repeated++;
                }
                else if (s.Status == "Corrected") c.Corrected++;
            }
            return map;
        }

        /// <summary>Revision answers per topic, kept apart from the original accuracy. Only attributable
        /// when no exam/kind filter is set (a revision answer is not tied to an exam or test kind).</summary>
        private async Task<Dictionary<string, RevisionOutcomeDto>?> LoadRevisionOutcomesAsync(Guid userId, ProgressFilter f, CancellationToken ct)
        {
            if (f.HasExamOrKindFilter) return null;
            var (from, _) = Range(f);
            var q = _db.RevisionAttempts.AsNoTracking().Where(r => r.UserId == userId);
            if (from.HasValue) q = q.Where(r => r.AnsweredAt >= from.Value);

            var rows = await q.GroupBy(r => new { r.SubjectSnapshot, r.TopicSnapshot })
                .Select(g => new { g.Key.SubjectSnapshot, g.Key.TopicSnapshot, Attempted = g.Count(), Correct = g.Count(r => r.IsCorrect) })
                .ToListAsync(ct);

            var map = new Dictionary<string, RevisionOutcomeDto>(StringComparer.Ordinal);
            foreach (var r in rows)
            {
                var key = Taxonomy.TopicKey(r.SubjectSnapshot, r.TopicSnapshot);
                if (!map.TryGetValue(key, out var o)) map[key] = o = new RevisionOutcomeDto();
                o.Attempted += r.Attempted;
                o.Correct += r.Correct;
            }
            foreach (var o in map.Values) o.AccuracyPercent = AnalyticsCalculator.Percent(o.Correct, o.Attempted);
            return map;
        }

        private TopicInsightDto ToTopicInsight(Bucket b, MistakeCounts? mistakes, RevisionOutcomeDto? revision,
            PracticeTargetResolver resolver, Guid? examId, decimal? paceRatio, DateTime utcNow)
        {
            var all = b.All;
            var inputs = new TopicInputs(
                all.Attempted, all.Correct, ToWindow(b.Recent), ToWindow(b.Previous), all.Last, paceRatio,
                mistakes?.Open ?? 0, mistakes?.Repeated ?? 0);
            var verdict = AnalyticsCalculator.Classify(inputs, _o, utcNow);

            return new TopicInsightDto
            {
                Subject = b.Subject,
                Topic = b.Topic,
                IsUnclassified = b.SubjectBlank || b.TopicBlank,
                Attempted = all.Attempted,
                Correct = all.Correct,
                Incorrect = all.Attempted - all.Correct,
                Skipped = all.Total - all.Attempted,
                AccuracyPercent = AnalyticsCalculator.Percent(all.Correct, all.Attempted),
                AvgSecondsPerAnswered = AnalyticsCalculator.AverageSeconds(all.TimeSecs, all.Timed),
                TimedQuestions = all.Timed,
                Status = verdict.Status.ToString(),
                Confidence = verdict.Confidence.ToString(),
                LastAttemptedAt = all.Last,
                Trend = BuildWindowChange(b),
                OpenMistakes = mistakes?.Open ?? 0,
                CorrectedMistakes = mistakes?.Corrected ?? 0,
                Revision = revision,
                Recommendations = verdict.Recommendations
                    .Select(r => new RecommendationDto
                    {
                        Code = r.Code,
                        Title = r.Title,
                        Reason = r.Reason,
                        ActionType = r.ActionType,
                        Subject = b.SubjectBlank ? null : b.Subject,
                        Topic = b.TopicBlank ? null : b.Topic
                    }).ToList(),
                PracticeTarget = resolver.Resolve(b.Subject, b.Topic, examId)
            };
        }

        private static int StatusRank(string status) => status switch
        {
            "NeedsAttention" => 0,
            "NeedsRetesting" => 1,
            "PracticeRecommended" => 2,
            "Improving" => 3,
            "PerformingWell" => 4,
            _ => 5
        };

        public async Task<List<TopicInsightDto>> GetTopicInsightsAsync(Guid userId, ProgressFilter f, CancellationToken ct)
        {
            var buckets = await BuildBucketsAsync(userId, f, ct);
            return await ToInsightsAsync(userId, f, buckets, ct);
        }

        private async Task<List<TopicInsightDto>> ToInsightsAsync(Guid userId, ProgressFilter f, Dictionary<string, Bucket> buckets, CancellationToken ct)
        {
            var mistakes = await LoadMistakeCountsAsync(userId, f, ct);
            var revision = await LoadRevisionOutcomesAsync(userId, f, ct);
            var resolver = await PracticeTargetResolver.LoadAsync(_db, ct);
            var now = DateTime.UtcNow;

            return buckets
                .Select(kv =>
                {
                    mistakes.TryGetValue(kv.Key, out var m);
                    RevisionOutcomeDto? rev = null;
                    revision?.TryGetValue(kv.Key, out rev);
                    return ToTopicInsight(kv.Value, m, rev, resolver, f.ExamId, null, now);
                })
                .OrderBy(t => StatusRank(t.Status))
                .ThenBy(t => t.AccuracyPercent ?? 101m)
                .ThenByDescending(t => t.Attempted)
                .ThenBy(t => t.Subject, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Topic, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private SubjectInsightDto ToSubjectInsight(string subjectName, bool blank, List<Bucket> parts, List<TopicInsightDto> topics, bool includeTopics)
        {
            var all = new WindowStats(0, 0);
            var recent = new WindowStats(0, 0);
            var previous = new WindowStats(0, 0);
            int total = 0, timed = 0;
            long secs = 0;
            foreach (var p in parts)
            {
                all += ToWindow(p.All);
                recent += ToWindow(p.Recent);
                previous += ToWindow(p.Previous);
                total += p.All.Total;
                timed += p.All.Timed;
                secs += p.All.TimeSecs;
            }
            var win = Math.Max(1, _o.ComparisonWindowDays);
            return new SubjectInsightDto
            {
                Subject = subjectName,
                IsUnclassified = blank,
                Attempted = all.Attempted,
                Correct = all.Correct,
                Incorrect = all.Attempted - all.Correct,
                Skipped = total - all.Attempted,
                AccuracyPercent = all.Accuracy,
                AvgSecondsPerAnswered = AnalyticsCalculator.AverageSeconds(secs, timed),
                Confidence = AnalyticsCalculator.ConfidenceFor(all.Attempted, _o).ToString(),
                Trend = BuildChange(recent, previous, $"Last {win} days", $"Previous {win} days"),
                TopicCount = topics.Count,
                WeakTopicCount = topics.Count(t => t.Status == "NeedsAttention" || t.Status == "PracticeRecommended" || t.Status == "NeedsRetesting"),
                Topics = includeTopics ? topics : new List<TopicInsightDto>()
            };
        }

        private List<SubjectInsightDto> BuildSubjects(Dictionary<string, Bucket> buckets, List<TopicInsightDto> insights, bool includeTopics)
        {
            var bySubject = buckets.Values.GroupBy(b => Taxonomy.SubjectKey(b.SubjectBlank ? null : b.Subject), StringComparer.Ordinal);
            var result = new List<SubjectInsightDto>();
            foreach (var g in bySubject)
            {
                var first = g.First();
                var topicInsights = insights
                    .Where(t => Taxonomy.SubjectKey(t.IsUnclassified && t.Subject == Taxonomy.Unclassified ? null : t.Subject) == g.Key)
                    .ToList();
                result.Add(ToSubjectInsight(first.Subject, first.SubjectBlank, g.ToList(), topicInsights, includeTopics));
            }

            // Subjects with enough data first (weakest accuracy first), thin ones after, Unclassified last.
            return result
                .OrderBy(s => s.IsUnclassified ? 1 : 0)
                .ThenBy(s => s.Confidence == "Insufficient" ? 1 : 0)
                .ThenBy(s => s.AccuracyPercent ?? 101m)
                .ThenBy(s => s.Subject, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private DataQualityDto BuildDataQuality(Dictionary<string, Bucket> buckets)
        {
            var dq = new DataQualityDto();
            foreach (var b in buckets.Values)
            {
                dq.TotalAnswers += b.All.Total;
                if (b.SubjectBlank) dq.AnswersWithoutSubject += b.All.Total;
                if (b.TopicBlank) dq.AnswersWithoutTopic += b.All.Total;
            }
            if (dq.AnswersWithoutTopic > 0)
                dq.Notes.Add($"{dq.AnswersWithoutTopic} of {dq.TotalAnswers} answered/skipped questions have no topic, so they are grouped under \"Unclassified\" and excluded from weak-topic advice.");
            if (dq.AnswersWithoutSubject > 0)
                dq.Notes.Add($"{dq.AnswersWithoutSubject} of {dq.TotalAnswers} questions have no subject.");
            return dq;
        }

        private static ProgressFiltersDto ToFiltersDto(ProgressFilter f) => new()
        {
            ExamId = f.ExamId,
            Kind = f.Kind?.ToString(),
            Days = f.Days,
            MyExamsOnly = f.MyExamsOnly
        };

        // ------------------------------------------------------------------ subjects / topic detail

        public async Task<SubjectsResponseDto> GetSubjectsAsync(Guid userId, ProgressFilter f, bool includeTopics, CancellationToken ct)
        {
            var buckets = await BuildBucketsAsync(userId, f, ct);
            var insights = await ToInsightsAsync(userId, f, buckets, ct);
            return new SubjectsResponseDto
            {
                Filters = ToFiltersDto(f),
                Subjects = BuildSubjects(buckets, insights, includeTopics),
                DataQuality = BuildDataQuality(buckets)
            };
        }

        public async Task<TopicDetailDto?> GetTopicDetailAsync(Guid userId, ProgressFilter f, string subject, string topic, CancellationToken ct)
        {
            subject = subject ?? string.Empty;
            topic = topic ?? string.Empty;
            var key = Taxonomy.TopicKey(subject, topic);
            // Taxonomy.Unclassified means "no value recorded" -- Taxonomy.TopicKey already treats the literal as a name,
            // so map it back to a blank for lookup.
            var subjectBlank = string.Equals(subject.Trim(), Taxonomy.Unclassified, StringComparison.OrdinalIgnoreCase);
            var topicBlank = string.Equals(topic.Trim(), Taxonomy.Unclassified, StringComparison.OrdinalIgnoreCase);
            key = Taxonomy.TopicKey(subjectBlank ? null : subject, topicBlank ? null : topic);

            var buckets = await BuildBucketsAsync(userId, f, ct);
            if (!buckets.ContainsKey(key)) return null;
            var insights = await ToInsightsAsync(userId, f, buckets, ct);
            var insight = insights.FirstOrDefault(t =>
                Taxonomy.TopicKey(t.IsUnclassified && t.Subject == Taxonomy.Unclassified ? null : t.Subject,
                                  t.IsUnclassified && t.Topic == Taxonomy.Unclassified ? null : t.Topic) == key);
            if (insight == null) return null;

            var (from, to) = Range(f);
            var q = AttemptQueries.CompletedAnswers(_db, userId, f.Kind, f.ExamIds, from, to);
            q = subjectBlank
                ? q.Where(a => a.SubjectSnapshot == null || a.SubjectSnapshot.Trim() == "")
                : q.Where(a => a.SubjectSnapshot != null && a.SubjectSnapshot.Trim() == subject.Trim());
            q = topicBlank
                ? q.Where(a => a.TopicSnapshot == null || a.TopicSnapshot.Trim() == "")
                : q.Where(a => a.TopicSnapshot != null && a.TopicSnapshot.Trim() == topic.Trim());

            var points = await q
                .GroupBy(a => new { a.StudentTestResultId, a.StudentTestResult!.AttemptedAt, a.StudentTestResult.TestKind })
                .Select(g => new
                {
                    AttemptId = g.Key.StudentTestResultId,
                    Date = g.Key.AttemptedAt,
                    Kind = g.Key.TestKind,
                    Attempted = g.Count(a => a.SelectedOption != null),
                    Correct = g.Count(a => a.SelectedOption != null && a.IsCorrect)
                })
                .OrderByDescending(p => p.Date)
                .Take(20)
                .ToListAsync(ct);

            var detail = new TopicDetailDto
            {
                Topic = insight,
                OpenMistakes = insight.OpenMistakes,
                CorrectedMistakes = insight.CorrectedMistakes,
                Trend = points
                    .OrderBy(p => p.Date)
                    .Select(p => new TopicAttemptPointDto
                    {
                        AttemptId = p.AttemptId,
                        Date = p.Date,
                        TestKind = p.Kind.ToString(),
                        Attempted = p.Attempted,
                        Correct = p.Correct,
                        AccuracyPercent = AnalyticsCalculator.Percent(p.Correct, p.Attempted)
                    }).ToList()
            };

            var states = await _revision.GetMistakeStatesAsync(userId, new MistakeQuery
            {
                ExamIds = f.ExamIds,
                Kind = f.Kind,
                From = from,
                To = to,
                Subject = subject.Trim(),
                Topic = topic.Trim()
            }, ct);
            detail.RepeatedMistakes = states.Count(s => s.Wrong > 0 && s.IsOpen && s.IsRepeated);
            return detail;
        }

        // ------------------------------------------------------------------ overview

        private static decimal? ScorePercent(decimal score, int total)
            => total <= 0 ? null : Math.Round(score / total * 100m, 1, MidpointRounding.AwayFromZero);

        private IQueryable<StudentTestResult> WithTitleIncludes(IQueryable<StudentTestResult> q) => q
            .Include(r => r.MockTest)
            .Include(r => r.PracticeTestTemplate)
            .Include(r => r.Paper).ThenInclude(p => p!.Exam)
            .Include(r => r.Quiz);

        public async Task<ProgressOverviewDto> GetOverviewAsync(Guid userId, ProgressFilter f, CancellationToken ct)
        {
            var (from, to) = Range(f);
            var buckets = await BuildBucketsAsync(userId, f, ct);
            var insights = await ToInsightsAsync(userId, f, buckets, ct);
            var subjects = BuildSubjects(buckets, insights, includeTopics: false);

            var dto = new ProgressOverviewDto
            {
                GeneratedAt = DateTime.UtcNow,
                Filters = ToFiltersDto(f),
                Subjects = subjects,
                DataQuality = BuildDataQuality(buckets)
            };

            // ---- totals
            var totals = dto.Totals;
            int total = 0, timed = 0;
            long secs = 0;
            foreach (var b in buckets.Values)
            {
                total += b.All.Total;
                totals.QuestionsAnswered += b.All.Attempted;
                totals.Correct += b.All.Correct;
                timed += b.All.Timed;
                secs += b.All.TimeSecs;
            }
            totals.Incorrect = totals.QuestionsAnswered - totals.Correct;
            totals.Skipped = total - totals.QuestionsAnswered;
            totals.AccuracyPercent = AnalyticsCalculator.Percent(totals.Correct, totals.QuestionsAnswered);
            totals.AvgSecondsPerAnswered = AnalyticsCalculator.AverageSeconds(secs, timed);

            var attempts = AttemptQueries.CompletedAttempts(_db, userId, f.Kind, f.ExamIds, from, to);
            totals.CompletedAssessments = await attempts.CountAsync(ct);
            var byKind = await attempts
                .GroupBy(r => r.TestKind)
                .Select(g => new { Kind = g.Key, Count = g.Count(), Correct = g.Sum(r => r.CorrectCount), Wrong = g.Sum(r => r.WrongCount) })
                .ToListAsync(ct);
            totals.ByKind = byKind
                .OrderBy(k => k.Kind.ToString(), StringComparer.Ordinal)
                .Select(k => new KindCountDto
                {
                    TestKind = k.Kind.ToString(),
                    Count = k.Count,
                    // Pooled accuracy: all correct / all answered across that type's attempts.
                    AvgAccuracyPercent = AnalyticsCalculator.Percent(k.Correct, k.Correct + k.Wrong)
                }).ToList();

            // ---- rolling comparisons (independent of the Days filter, respect exam/kind)
            var (l7, l30) = await LoadRollingChangesAsync(userId, f.Kind, f.ExamIds, ct);
            dto.Last7Days = l7;
            dto.Last30Days = l30;

            // ---- recent activity + score trend
            var recent = await WithTitleIncludes(attempts)
                .OrderByDescending(r => r.AttemptedAt).ThenBy(r => r.Id)
                .Take(5)
                .ToListAsync(ct);
            dto.RecentActivity = recent.Select(ToRecent).ToList();

            var trendRows = await attempts
                .OrderByDescending(r => r.AttemptedAt).ThenBy(r => r.Id)
                .Take(10)
                .Select(r => new { r.Id, r.AttemptedAt, r.TestKind, r.CorrectCount, r.WrongCount, r.SkippedCount, r.Score })
                .ToListAsync(ct);
            dto.ScoreTrend = trendRows.OrderBy(r => r.AttemptedAt).ThenBy(r => r.Id).Select(r => new TrendPointDto
            {
                AttemptId = r.Id,
                Date = r.AttemptedAt,
                TestKind = r.TestKind.ToString(),
                Attempted = r.CorrectCount + r.WrongCount,
                AccuracyPercent = AnalyticsCalculator.Percent(r.CorrectCount, r.CorrectCount + r.WrongCount),
                ScorePercent = ScorePercent(r.Score, r.CorrectCount + r.WrongCount + r.SkippedCount)
            }).ToList();

            // ---- weak topics (topic-level verdicts; fully unclassified topics give no actionable advice)
            dto.WeakTopics = insights
                .Where(t => !t.IsUnclassified
                            && (t.Status == "NeedsAttention" || t.Status == "NeedsRetesting" || t.Status == "PracticeRecommended"))
                .Take(5)
                .ToList();
            dto.InsufficientDataTopicCount = insights.Count(t => t.Status == "InsufficientData");

            // ---- revision + streak (the study plan summary is attached by the controller)
            dto.Revision = await _revision.GetSummaryAsync(userId, ct);

            var streak = await _db.UserStreaks.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
            dto.CurrentStreak = streak?.CurrentStreak ?? 0;
            dto.LongestStreak = streak?.LongestStreak ?? 0;

            var weekAgo = DateTime.UtcNow.AddDays(-7);
            var attemptTimes = await AttemptQueries.CompletedAttempts(_db, userId, null, null, weekAgo, null)
                .Select(r => r.AttemptedAt).ToListAsync(ct);
            var revisionTimes = await _db.RevisionAttempts.AsNoTracking()
                .Where(r => r.UserId == userId && r.AnsweredAt >= weekAgo).Select(r => r.AnsweredAt).ToListAsync(ct);
            dto.ActiveDaysLast7 = attemptTimes.Concat(revisionTimes).Select(AnalyticsCalculator.IstDay).Distinct().Count();

            return dto;
        }

        private static RecentAssessmentDto ToRecent(StudentTestResult r)
        {
            var answered = r.CorrectCount + r.WrongCount;
            var totalQ = answered + r.SkippedCount;
            return new RecentAssessmentDto
            {
                AttemptId = r.Id,
                TestKind = r.TestKind.ToString(),
                Title = TestAttemptsController.TitleFor(r),
                SubmittedAt = r.AttemptedAt,
                Score = r.Score,
                TotalQuestions = totalQ,
                AccuracyPercent = AnalyticsCalculator.Percent(r.CorrectCount, answered),
                ScorePercent = ScorePercent(r.Score, totalQ),
                TimeTakenSeconds = r.TimeTakenSeconds
            };
        }

        // ------------------------------------------------------------------ trends

        public async Task<TrendsDto> GetTrendsAsync(Guid userId, ProgressFilter f, CancellationToken ct)
        {
            var dto = new TrendsDto { Filters = ToFiltersDto(f) };
            var (l7, l30) = await LoadRollingChangesAsync(userId, f.Kind, f.ExamIds, ct);
            dto.Last7Days = l7;
            dto.Last30Days = l30;

            var attempts = AttemptQueries.CompletedAttempts(_db, userId, f.Kind, f.ExamIds, null, null);
            var last30 = await attempts
                .OrderByDescending(r => r.AttemptedAt).ThenBy(r => r.Id)
                .Take(30)
                .Select(r => new { r.Id, r.AttemptedAt, r.TestKind, r.CorrectCount, r.WrongCount, r.SkippedCount, r.Score })
                .ToListAsync(ct);
            dto.Attempts = last30.OrderBy(r => r.AttemptedAt).ThenBy(r => r.Id).Select(r => new TrendPointDto
            {
                AttemptId = r.Id,
                Date = r.AttemptedAt,
                TestKind = r.TestKind.ToString(),
                Attempted = r.CorrectCount + r.WrongCount,
                AccuracyPercent = AnalyticsCalculator.Percent(r.CorrectCount, r.CorrectCount + r.WrongCount),
                ScorePercent = ScorePercent(r.Score, r.CorrectCount + r.WrongCount + r.SkippedCount)
            }).ToList();

            // Last 12 IST weeks. Weekly numbers come from the stored per-attempt counts (no answer rows needed).
            var nowIst = DateOnly.FromDateTime(DateTime.UtcNow + AnalyticsCalculator.Ist);
            var thisMonday = nowIst.AddDays(-(((int)nowIst.DayOfWeek + 6) % 7));
            var firstMonday = thisMonday.AddDays(-7 * 11);
            var fromUtc = AnalyticsCalculator.DayBoundsUtc(firstMonday).StartUtc;

            var rows = await attempts.Where(r => r.AttemptedAt >= fromUtc)
                .Select(r => new { r.AttemptedAt, r.CorrectCount, r.WrongCount, r.TimeTakenSeconds })
                .ToListAsync(ct);

            dto.Weekly = rows
                .GroupBy(r =>
                {
                    var d = AnalyticsCalculator.IstDay(r.AttemptedAt);
                    return d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
                })
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var attempted = g.Sum(r => r.CorrectCount + r.WrongCount);
                    var correct = g.Sum(r => r.CorrectCount);
                    return new WeeklyPointDto
                    {
                        WeekStart = g.Key.ToString("yyyy-MM-dd"),
                        Attempted = attempted,
                        Correct = correct,
                        AccuracyPercent = AnalyticsCalculator.Percent(correct, attempted),
                        // Whole-attempt time divided by answered questions (per-question timings are used on the result page).
                        AvgSecondsPerAnswered = AnalyticsCalculator.AverageSeconds(g.Sum(r => (long)r.TimeTakenSeconds), attempted)
                    };
                })
                .Where(w => w.Attempted > 0)
                .ToList();
            return dto;
        }

        // ------------------------------------------------------------------ one assessment

        private sealed class AnswerRow
        {
            public int QuestionOrder { get; set; }
            public string? Subject { get; set; }
            public string? Topic { get; set; }
            public bool Answered { get; set; }
            public bool IsCorrect { get; set; }
            public int? Seconds { get; set; }
        }

        private async Task<List<AnswerRow>> LoadAnswerRowsAsync(Guid attemptId, CancellationToken ct)
        {
            var max = _o.MaxSecondsPerQuestion;
            var rows = await _db.StudentAnswers.AsNoTracking()
                .Where(a => a.StudentTestResultId == attemptId)
                .OrderBy(a => a.QuestionOrder)
                .Select(a => new AnswerRow
                {
                    QuestionOrder = a.QuestionOrder,
                    Subject = a.SubjectSnapshot,
                    Topic = a.TopicSnapshot,
                    Answered = a.SelectedOption != null,
                    IsCorrect = a.SelectedOption != null && a.IsCorrect,
                    Seconds = a.TimeSpentSeconds
                })
                .ToListAsync(ct);
            foreach (var r in rows)
                if (r.Seconds.HasValue) r.Seconds = Math.Clamp(r.Seconds.Value, 0, max);
            return rows;
        }

        private static int AllottedSecondsFor(StudentTestResult a)
        {
            var minutes = a.TestKind switch
            {
                TestKind.Mock => a.MockTest?.DurationMinutes ?? 0,
                TestKind.PreviousYearPaper => a.Paper?.DurationMinutes ?? 0,
                TestKind.Quiz => a.Quiz?.DurationMinutes ?? a.QuizDurationMinutes ?? 0,
                _ => a.PracticeTestTemplate?.DurationMinutes ?? a.PracticeDurationMinutes ?? 0
            };
            return Math.Max(0, minutes) * 60;
        }

        private sealed class SubjectAgg
        {
            public string Name { get; set; } = Taxonomy.Unclassified;
            public bool Blank { get; set; }
            public List<AnswerRow> Rows { get; } = new();
        }

        private static List<SubjectAgg> GroupBySubject(List<AnswerRow> rows)
        {
            var map = new Dictionary<string, SubjectAgg>(StringComparer.Ordinal);
            foreach (var r in rows)
            {
                var key = Taxonomy.SubjectKey(r.Subject);
                if (!map.TryGetValue(key, out var s))
                    map[key] = s = new SubjectAgg { Name = Taxonomy.SubjectName(r.Subject), Blank = Taxonomy.IsUnclassified(r.Subject) };
                s.Rows.Add(r);
            }
            return map.Values.ToList();
        }

        public async Task<AttemptAnalyticsDto?> GetAttemptAnalyticsAsync(Guid userId, Guid attemptId, CancellationToken ct)
        {
            var attempt = await WithTitleIncludes(_db.StudentTestResults.AsNoTracking().WhereReleased())
                .FirstOrDefaultAsync(r => r.Id == attemptId && r.UserId == userId && r.Status != TestAttemptStatus.InProgress, ct);
            if (attempt == null) return null; // not found, not yours, or not finished: all look identical to the caller

            var rows = await LoadAnswerRowsAsync(attemptId, ct);
            var ratio = attempt.NegativeMarkingRatio;
            var allottedSeconds = AllottedSecondsFor(attempt);

            // ---- summary
            var total = rows.Count;
            var attempted = rows.Count(r => r.Answered);
            var correct = rows.Count(r => r.IsCorrect);
            var summary = new AssessmentSummaryDto
            {
                TotalQuestions = total,
                Attempted = attempted,
                Correct = correct,
                Incorrect = attempted - correct,
                Unattempted = total - attempted,
                Score = attempt.Score,
                MaxScore = total,
                NegativeMarkingRatio = ratio,
                AccuracyPercent = AnalyticsCalculator.Percent(correct, attempted),
                ScorePercent = ScorePercent(attempt.Score, total),
                TimeTakenSeconds = attempt.TimeTakenSeconds,
                AllottedSeconds = allottedSeconds,
                AvgSecondsPerAttempted = AnalyticsCalculator.AverageSeconds(attempt.TimeTakenSeconds, attempted)
            };

            var dto = new AttemptAnalyticsDto
            {
                AttemptId = attempt.Id,
                TestKind = attempt.TestKind.ToString(),
                Title = TestAttemptsController.TitleFor(attempt),
                Status = attempt.Status.ToString(),
                SubmittedAt = attempt.AttemptedAt,
                Summary = summary
            };

            // ---- time analysis (first: topic recommendations use the pace)
            decimal? allottedPerQuestion = total > 0 && allottedSeconds > 0 ? (decimal)allottedSeconds / total : null;
            dto.Time = BuildTimeAnalysis(rows, allottedPerQuestion);

            // ---- history-level verdict per topic (the whole-history view, not just this attempt)
            var historyFilter = new ProgressFilter();
            var buckets = await BuildBucketsAsync(userId, historyFilter, ct);
            var mistakes = await LoadMistakeCountsAsync(userId, historyFilter, ct);
            var revision = await LoadRevisionOutcomesAsync(userId, historyFilter, ct);
            var resolver = await PracticeTargetResolver.LoadAsync(_db, ct);
            var now = DateTime.UtcNow;

            // ---- previous comparable attempt (needed for per-subject change)
            var previous = await FindPreviousComparableAsync(userId, attempt, ct);
            List<AnswerRow>? previousRows = null;
            if (previous != null) previousRows = await LoadAnswerRowsAsync(previous.Id, ct);
            var prevSubjects = previousRows == null
                ? new Dictionary<string, SubjectAgg>(StringComparer.Ordinal)
                : GroupBySubject(previousRows).ToDictionary(s => Taxonomy.SubjectKey(s.Blank ? null : s.Name), s => s, StringComparer.Ordinal);

            // ---- subjects -> topics
            foreach (var s in GroupBySubject(rows).OrderBy(s => s.Blank ? 1 : 0).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                var sAttempted = s.Rows.Count(r => r.Answered);
                var sCorrect = s.Rows.Count(r => r.IsCorrect);
                var sTimed = s.Rows.Where(r => r.Answered && r.Seconds.HasValue).ToList();
                var sSecs = sTimed.Sum(r => (long)r.Seconds!.Value);
                var subjectDto = new AttemptSubjectDto
                {
                    Subject = s.Name,
                    IsUnclassified = s.Blank,
                    TotalQuestions = s.Rows.Count,
                    Attempted = sAttempted,
                    Correct = sCorrect,
                    Incorrect = sAttempted - sCorrect,
                    Unattempted = s.Rows.Count - sAttempted,
                    MarksObtained = AnalyticsCalculator.Marks(sCorrect, sAttempted - sCorrect, ratio),
                    MaxMarks = s.Rows.Count,
                    AccuracyPercent = AnalyticsCalculator.Percent(sCorrect, sAttempted),
                    TimedQuestions = sTimed.Count,
                    TimeSpentSeconds = (int)Math.Min(sSecs, int.MaxValue),
                    AvgSecondsPerAttempted = AnalyticsCalculator.AverageSeconds(sSecs, sTimed.Count),
                    Confidence = AnalyticsCalculator.ConfidenceFor(sAttempted, _o).ToString()
                };

                if (prevSubjects.TryGetValue(Taxonomy.SubjectKey(s.Blank ? null : s.Name), out var ps))
                {
                    var pAtt = ps.Rows.Count(r => r.Answered);
                    var pCor = ps.Rows.Count(r => r.IsCorrect);
                    var pAcc = AnalyticsCalculator.Percent(pCor, pAtt);
                    subjectDto.PreviousAttempted = pAtt;
                    subjectDto.PreviousAccuracyPercent = pAcc;
                    subjectDto.AccuracyChangePoints = AnalyticsCalculator.ChangePoints(subjectDto.AccuracyPercent, pAcc);
                }

                foreach (var t in s.Rows
                    .GroupBy(r => Taxonomy.TopicKey(r.Subject, r.Topic), StringComparer.Ordinal)
                    .OrderBy(g => Taxonomy.IsUnclassified(g.First().Topic) ? 1 : 0)
                    .ThenBy(g => Taxonomy.TopicName(g.First().Topic), StringComparer.OrdinalIgnoreCase))
                {
                    var first = t.First();
                    var tAttempted = t.Count(r => r.Answered);
                    var tCorrect = t.Count(r => r.IsCorrect);
                    var tTimed = t.Where(r => r.Answered && r.Seconds.HasValue).ToList();
                    var tSecs = tTimed.Sum(r => (long)r.Seconds!.Value);
                    var topicDto = new AttemptTopicDto
                    {
                        Subject = s.Name,
                        Topic = Taxonomy.TopicName(first.Topic),
                        IsUnclassified = Taxonomy.IsUnclassified(first.Subject) || Taxonomy.IsUnclassified(first.Topic),
                        TotalQuestions = t.Count(),
                        Attempted = tAttempted,
                        Correct = tCorrect,
                        Incorrect = tAttempted - tCorrect,
                        Unattempted = t.Count() - tAttempted,
                        MarksObtained = AnalyticsCalculator.Marks(tCorrect, tAttempted - tCorrect, ratio),
                        AccuracyPercent = AnalyticsCalculator.Percent(tCorrect, tAttempted),
                        TimedQuestions = tTimed.Count,
                        TimeSpentSeconds = (int)Math.Min(tSecs, int.MaxValue),
                        AvgSecondsPerAttempted = AnalyticsCalculator.AverageSeconds(tSecs, tTimed.Count)
                    };

                    if (buckets.TryGetValue(t.Key, out var bucket))
                    {
                        decimal? pace = null;
                        if (dto.Time.Available && tTimed.Count >= _o.MinQuestionsPerComparisonWindow)
                            pace = AnalyticsCalculator.PaceRatio(topicDto.AvgSecondsPerAttempted, allottedPerQuestion);
                        mistakes.TryGetValue(t.Key, out var m);
                        RevisionOutcomeDto? rev = null;
                        revision?.TryGetValue(t.Key, out rev);
                        topicDto.Overall = ToTopicInsight(bucket, m, rev, resolver, null, pace, now);
                    }
                    subjectDto.Topics.Add(topicDto);
                }
                dto.Subjects.Add(subjectDto);
            }

            // ---- improvement
            dto.Improvement = await BuildImprovementAsync(userId, attempt, summary, previous, previousRows, rows, ct);

            // ---- recommendations: most urgent topics that actually had mistakes in THIS attempt
            var recs = new List<RecommendationDto>();
            foreach (var topic in dto.Subjects.SelectMany(s => s.Topics)
                .Where(t => t.Incorrect > 0 && t.Overall != null && !t.IsUnclassified)
                .OrderBy(t => StatusRank(t.Overall!.Status))
                .ThenBy(t => t.AccuracyPercent ?? 101m)
                .Take(3))
            {
                var primary = topic.Overall!.Recommendations.FirstOrDefault(r => r.Code != "REVISE_MISTAKES")
                              ?? topic.Overall.Recommendations.FirstOrDefault();
                if (primary != null) recs.Add(primary);
                var timeRec = topic.Overall.Recommendations.FirstOrDefault(r => r.Code == "PRACTISE_TIMED" || r.Code == "SLOW_DOWN");
                if (timeRec != null && !recs.Contains(timeRec)) recs.Add(timeRec);
            }
            var openInAttempt = dto.Subjects.Sum(s => s.Incorrect);
            if (openInAttempt > 0)
                recs.Add(new RecommendationDto
                {
                    Code = "REVIEW_THIS_ATTEMPT",
                    Title = "Review the questions you got wrong",
                    Reason = $"You answered {openInAttempt} question{(openInAttempt == 1 ? "" : "s")} incorrectly here; they are in your Mistake Book for revision.",
                    ActionType = "Revise"
                });
            dto.Recommendations = recs;

            // ---- notes
            var unclassified = rows.Count(r => Taxonomy.IsUnclassified(r.Subject) || Taxonomy.IsUnclassified(r.Topic));
            if (unclassified > 0)
                dto.Notes.Add($"{unclassified} of {total} questions have no subject/topic, so they are shown as Unclassified and left out of topic advice.");
            if (!dto.Time.Available && !string.IsNullOrEmpty(dto.Time.Note))
                dto.Notes.Add(dto.Time.Note!);
            return dto;
        }

        private TimeAnalysisDto BuildTimeAnalysis(List<AnswerRow> rows, decimal? allottedPerQuestion)
        {
            var answered = rows.Where(r => r.Answered).ToList();
            var timedAnswered = answered.Where(r => r.Seconds.HasValue).ToList();
            var t = new TimeAnalysisDto
            {
                AllottedSecondsPerQuestion = allottedPerQuestion.HasValue ? Math.Round(allottedPerQuestion.Value, 1) : null,
                TrackedSeconds = rows.Where(r => r.Seconds.HasValue).Sum(r => r.Seconds!.Value)
            };

            if (answered.Count == 0)
            {
                t.Note = "No questions were answered, so there is no per-question time to analyse.";
                return t;
            }
            t.CoveragePercent = (int)Math.Round(timedAnswered.Count * 100m / answered.Count, MidpointRounding.AwayFromZero);
            if (timedAnswered.Count == 0)
            {
                t.Note = "Per-question timings were not recorded for this attempt (older attempts and some devices don't report them).";
                return t;
            }
            if (t.CoveragePercent < _o.MinTimingCoveragePercent)
            {
                t.Note = $"Per-question timings were recorded for only {t.CoveragePercent}% of answered questions (at least {_o.MinTimingCoveragePercent}% is needed), so time analysis is withheld.";
                return t;
            }

            t.Available = true;
            var secs = timedAnswered.Select(r => r.Seconds!.Value).ToList();
            t.MedianSecondsPerAttempted = AnalyticsCalculator.Median(secs);
            t.AvgSecondsPerAttempted = AnalyticsCalculator.AverageSeconds(secs.Sum(x => (long)x), secs.Count);
            var right = timedAnswered.Where(r => r.IsCorrect).Select(r => (long)r.Seconds!.Value).ToList();
            var wrong = timedAnswered.Where(r => !r.IsCorrect).Select(r => (long)r.Seconds!.Value).ToList();
            t.AvgSecondsCorrect = AnalyticsCalculator.AverageSeconds(right.Sum(), right.Count);
            t.AvgSecondsIncorrect = AnalyticsCalculator.AverageSeconds(wrong.Sum(), wrong.Count);

            t.PaceRatio = AnalyticsCalculator.PaceRatio(t.AvgSecondsPerAttempted, allottedPerQuestion);
            t.PaceLabel = AnalyticsCalculator.PaceLabel(t.PaceRatio, _o);
            if (!allottedPerQuestion.HasValue)
                t.Note = "This assessment has no recorded time limit, so SCORAM shows your times without calling them fast or slow.";
            if (t.CoveragePercent < 100)
                t.Note = (t.Note == null ? "" : t.Note + " ") + $"Timings cover {t.CoveragePercent}% of answered questions.";

            if (allottedPerQuestion.HasValue)
            {
                var fastLimit = allottedPerQuestion.Value * _o.FastIncorrectFraction;
                var slowLimit = allottedPerQuestion.Value * _o.UnusuallySlowMultiple;
                t.FastIncorrectThresholdSeconds = Math.Round(fastLimit, 1);
                t.FastIncorrectCount = timedAnswered.Count(r => !r.IsCorrect && r.Seconds!.Value <= fastLimit);

                t.SlowestQuestions = rows.Where(r => r.Seconds.HasValue)
                    .OrderByDescending(r => r.Seconds!.Value).ThenBy(r => r.QuestionOrder)
                    .Take(5)
                    .Select(r => new SlowQuestionDto
                    {
                        QuestionOrder = r.QuestionOrder,
                        Subject = Taxonomy.SubjectName(r.Subject),
                        Topic = Taxonomy.TopicName(r.Topic),
                        Seconds = r.Seconds!.Value,
                        IsCorrect = r.IsCorrect,
                        WasSkipped = !r.Answered,
                        IsUnusuallySlow = r.Seconds!.Value > slowLimit
                    }).ToList();
            }
            else
            {
                t.SlowestQuestions = rows.Where(r => r.Seconds.HasValue)
                    .OrderByDescending(r => r.Seconds!.Value).ThenBy(r => r.QuestionOrder)
                    .Take(5)
                    .Select(r => new SlowQuestionDto
                    {
                        QuestionOrder = r.QuestionOrder,
                        Subject = Taxonomy.SubjectName(r.Subject),
                        Topic = Taxonomy.TopicName(r.Topic),
                        Seconds = r.Seconds!.Value,
                        IsCorrect = r.IsCorrect,
                        WasSkipped = !r.Answered,
                        IsUnusuallySlow = false
                    }).ToList();
            }

            t.BySubject = GroupBySubject(rows).Select(s =>
            {
                var timed = s.Rows.Where(r => r.Answered && r.Seconds.HasValue).ToList();
                var sum = timed.Sum(r => (long)r.Seconds!.Value);
                return new SubjectTimeDto
                {
                    Subject = s.Name,
                    TimedQuestions = timed.Count,
                    TimeSpentSeconds = (int)Math.Min(sum, int.MaxValue),
                    AvgSecondsPerAttempted = AnalyticsCalculator.AverageSeconds(sum, timed.Count)
                };
            }).OrderBy(s => s.Subject, StringComparer.OrdinalIgnoreCase).ToList();
            return t;
        }

        // ------------------------------------------------------------------ improvement / comparison

        /// <summary>The latest EARLIER completed attempt of the same assessment: same test kind AND same
        /// source (same Mock Test / Paper / Quiz / Template, or for ad-hoc Practice the same exam + subject +
        /// topic selection). Attempts with no stable source (a generated Weak-Topics quiz) have none.</summary>
        private async Task<StudentTestResult?> FindPreviousComparableAsync(Guid userId, StudentTestResult a, CancellationToken ct)
        {
            var q = _db.StudentTestResults.AsNoTracking().WhereReleased()
                .Where(r => r.UserId == userId && r.Id != a.Id && r.Status != TestAttemptStatus.InProgress && r.TestKind == a.TestKind
                            && r.AttemptedAt < a.AttemptedAt);

            switch (a.TestKind)
            {
                case TestKind.Mock:
                    if (a.MockTestId == null) return null;
                    q = q.Where(r => r.MockTestId == a.MockTestId);
                    break;
                case TestKind.PreviousYearPaper:
                    if (a.PaperId == null) return null;
                    q = q.Where(r => r.PaperId == a.PaperId);
                    break;
                case TestKind.Quiz:
                    if (a.QuizId == null) return null;
                    q = q.Where(r => r.QuizId == a.QuizId);
                    break;
                default:
                    if (a.PracticeTestTemplateId != null)
                        q = q.Where(r => r.PracticeTestTemplateId == a.PracticeTestTemplateId);
                    else
                    {
                        // Ad-hoc practice: only comparable when it targeted the same exam/subject/topic selection.
                        if (a.PracticeExamId == null && a.PracticeSubjectId == null && a.PracticeTopicId == null) return null;
                        q = q.Where(r => r.PracticeTestTemplateId == null
                                         && r.PracticeExamId == a.PracticeExamId
                                         && r.PracticeSubjectId == a.PracticeSubjectId
                                         && r.PracticeTopicId == a.PracticeTopicId);
                    }
                    break;
            }

            return await WithTitleIncludes(q)
                .OrderByDescending(r => r.AttemptedAt).ThenByDescending(r => r.Id)
                .FirstOrDefaultAsync(ct);
        }

        private async Task<ImprovementDto> BuildImprovementAsync(Guid userId, StudentTestResult attempt, AssessmentSummaryDto summary,
            StudentTestResult? previous, List<AnswerRow>? previousRows, List<AnswerRow> rows, CancellationToken ct)
        {
            var imp = new ImprovementDto();

            // Rolling comparison for this assessment TYPE (always safe: same kind, accuracy in points).
            var now = DateTime.UtcNow;
            var c30 = await WindowAsync(userId, attempt.TestKind, null, now.AddDays(-30), null, ct);
            var p30 = await WindowAsync(userId, attempt.TestKind, null, now.AddDays(-60), now.AddDays(-30), ct);
            imp.Last30Days = BuildChange(c30, p30, "Last 30 days", "Previous 30 days");

            if (previous == null || previousRows == null)
            {
                imp.Comparable = false;
                imp.Basis = attempt.TestKind == TestKind.Quiz && attempt.QuizId == null
                    ? "Weak-topics quizzes are generated fresh each time, so two of them are not the same assessment."
                    : "No earlier attempt of this same assessment to compare with.";
                imp.Note = "Not enough comparable data yet.";
                return imp;
            }

            var pAnswered = previous.CorrectCount + previous.WrongCount;
            var pTotal = pAnswered + previous.SkippedCount;
            imp.Comparable = true;
            imp.Basis = $"Compared with your previous attempt of the same {DescribeKind(attempt.TestKind)} ({TestAttemptsController.TitleFor(previous)}).";
            imp.PreviousAttemptId = previous.Id;
            imp.PreviousSubmittedAt = previous.AttemptedAt;
            imp.PreviousTitle = TestAttemptsController.TitleFor(previous);
            imp.Previous = new AssessmentSummaryDto
            {
                TotalQuestions = pTotal,
                Attempted = pAnswered,
                Correct = previous.CorrectCount,
                Incorrect = previous.WrongCount,
                Unattempted = previous.SkippedCount,
                Score = previous.Score,
                MaxScore = pTotal,
                NegativeMarkingRatio = previous.NegativeMarkingRatio,
                AccuracyPercent = AnalyticsCalculator.Percent(previous.CorrectCount, pAnswered),
                ScorePercent = ScorePercent(previous.Score, pTotal),
                TimeTakenSeconds = previous.TimeTakenSeconds,
                AllottedSeconds = AllottedSecondsFor(previous),
                AvgSecondsPerAttempted = AnalyticsCalculator.AverageSeconds(previous.TimeTakenSeconds, pAnswered)
            };
            imp.AccuracyChangePoints = AnalyticsCalculator.ChangePoints(summary.AccuracyPercent, imp.Previous.AccuracyPercent);

            var sameMarking = previous.NegativeMarkingRatio == attempt.NegativeMarkingRatio && pTotal == summary.TotalQuestions;
            if (sameMarking)
                imp.ScorePercentChangePoints = AnalyticsCalculator.ChangePoints(summary.ScorePercent, imp.Previous.ScorePercent);

            var parts = new List<string>();
            if (imp.AccuracyChangePoints.HasValue)
            {
                var c = imp.AccuracyChangePoints.Value;
                parts.Add($"Accuracy {AnalyticsCalculator.Fmt(imp.Previous.AccuracyPercent ?? 0m)}% -> {AnalyticsCalculator.Fmt(summary.AccuracyPercent ?? 0m)}% ({(c > 0 ? "+" : "")}{AnalyticsCalculator.Fmt(c)} percentage points, {imp.Previous.Attempted} -> {summary.Attempted} questions answered).");
            }
            if (!sameMarking)
                parts.Add("Raw scores are not compared because the two attempts used different marking or a different number of questions.");
            imp.Note = parts.Count == 0 ? "Accuracy can't be compared because one attempt had no answered questions." : string.Join(" ", parts);

            // Per-subject change vs the previous attempt.
            var prevByKey = GroupBySubject(previousRows).ToDictionary(s => Taxonomy.SubjectKey(s.Blank ? null : s.Name), s => s, StringComparer.Ordinal);
            foreach (var cur in GroupBySubject(rows).OrderBy(s => s.Blank ? 1 : 0).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!prevByKey.TryGetValue(Taxonomy.SubjectKey(cur.Blank ? null : cur.Name), out var prev)) continue;
                var cAtt = cur.Rows.Count(r => r.Answered);
                var cCor = cur.Rows.Count(r => r.IsCorrect);
                var pAtt = prev.Rows.Count(r => r.Answered);
                var pCor = prev.Rows.Count(r => r.IsCorrect);
                var cAcc = AnalyticsCalculator.Percent(cCor, cAtt);
                var pAcc = AnalyticsCalculator.Percent(pCor, pAtt);
                imp.SubjectChanges.Add(new SubjectChangeDto
                {
                    Subject = cur.Name,
                    CurrentAccuracyPercent = cAcc,
                    CurrentAttempted = cAtt,
                    PreviousAccuracyPercent = pAcc,
                    PreviousAttempted = pAtt,
                    ChangePoints = AnalyticsCalculator.ChangePoints(cAcc, pAcc)
                });
            }
            return imp;
        }

        private static string DescribeKind(TestKind k) => k switch
        {
            TestKind.Mock => "mock test",
            TestKind.PreviousYearPaper => "previous year paper",
            TestKind.Quiz => "quiz",
            _ => "practice test"
        };

        // ------------------------------------------------------------------ recording per-question timings

        public async Task<TimingsOutcome> RecordTimingsAsync(Guid userId, Guid attemptId, TestTimingsDto dto, CancellationToken ct)
        {
            var attempt = await _db.StudentTestResults
                .Include(r => r.MockTest).Include(r => r.PracticeTestTemplate).Include(r => r.Paper).Include(r => r.Quiz)
                .FirstOrDefaultAsync(r => r.Id == attemptId && r.UserId == userId, ct);
            if (attempt == null) return new TimingsOutcome(null, 404, "Attempt not found.");
            if (attempt.Status != TestAttemptStatus.InProgress)
                return new TimingsOutcome(null, 409, "This attempt has already been submitted.");

            var items = (dto.Timings ?? new List<TestTimingItemDto>())
                .GroupBy(t => t.StudentAnswerId)
                .Select(g => g.OrderByDescending(x => x.TimeSpentSeconds).First()) // a running total only grows: keep the largest
                .ToList();
            if (items.Count == 0) return new TimingsOutcome(new TestTimingsResponseDto(), 200, null);

            var ids = items.Select(i => i.StudentAnswerId).ToList();
            var answers = await _db.StudentAnswers
                .Where(a => a.StudentTestResultId == attemptId && ids.Contains(a.Id))
                .ToListAsync(ct);
            var byId = answers.ToDictionary(a => a.Id);

            // Server-side plausibility: no single question, and not all questions together, can have more
            // seconds than the wall-clock time since the attempt started (plus a small network grace).
            var elapsed = (int)Math.Max(0, (DateTime.UtcNow - attempt.StartedAt).TotalSeconds) + 30;
            var perQuestionCap = Math.Min(_o.MaxSecondsPerQuestion, elapsed);
            var allottedSeconds = AllottedSecondsFor(attempt);
            if (allottedSeconds > 0) perQuestionCap = Math.Min(perQuestionCap, allottedSeconds + 60);

            var otherTotal = await _db.StudentAnswers
                .Where(a => a.StudentTestResultId == attemptId && !ids.Contains(a.Id))
                .SumAsync(a => (long?)a.TimeSpentSeconds ?? 0L, ct);

            long runningTotal = otherTotal;
            var result = new TestTimingsResponseDto();
            var accepted = new List<(StudentAnswer Answer, int Seconds)>();
            foreach (var item in items)
            {
                if (!byId.TryGetValue(item.StudentAnswerId, out var answer)) { result.Ignored++; continue; }
                // More seconds than could physically have elapsed (or than the per-question ceiling) is rejected,
                // not clamped: a clamped value would still be an invented number.
                if (item.TimeSpentSeconds < 0 || item.TimeSpentSeconds > perQuestionCap) { result.Ignored++; continue; }
                var seconds = item.TimeSpentSeconds;
                var existing = answer.TimeSpentSeconds ?? 0;
                if (seconds < existing) { result.Ignored++; runningTotal += existing; continue; } // totals never go backwards
                accepted.Add((answer, seconds));
                runningTotal += seconds;
            }

            if (runningTotal > elapsed)
            {
                // Implausible overall: accept nothing from this batch rather than store inflated times.
                result.Ignored += accepted.Count;
                return new TimingsOutcome(result, 200, null);
            }

            foreach (var (answer, seconds) in accepted)
            {
                answer.TimeSpentSeconds = seconds;
                result.Accepted++;
            }
            if (result.Accepted > 0) await _db.SaveChangesAsync(ct);
            return new TimingsOutcome(result, 200, null);
        }
    }
}
