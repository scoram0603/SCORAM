using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;

namespace ScoramAPI.Services
{
    // ADMIN ANALYTICS -- the single place dashboard KPIs, analytics charts and report exports get their
    // numbers, so a figure can never differ between the dashboard card, the chart and the exported file.
    //
    // DEFINITIONS (also surfaced to the UI as KPI tooltips):
    //   * Date ranges are whole calendar days in the ADMIN'S timezone, expressed as a fixed UTC offset
    //     (default +330 = IST, which has no daylight saving). The database stores UTC; a local day
    //     [D 00:00, D+1 00:00) is converted to a UTC half-open interval for filtering, and bucketing
    //     shifts timestamps by the same offset before taking the date. "Previous period" is the
    //     immediately preceding span of identical length.
    //   * "Active learner" = distinct student who STARTED at least one test attempt in the bucket. SCORAM
    //     only stores each student's LAST login time (User.LastActiveAt), not a login history, so a
    //     historical DAU/MAU series from logins is not reconstructible; learner activity is the honest
    //     per-day signal. "Active today / 30 days" KPIs use LastActiveAt and are point-in-time only.
    //   * "Completed" attempt = Submitted or AutoSubmitted. "Abandoned" = Expired. InProgress is reported
    //     separately so recent starts are not counted as abandoned.
    //   * "Accuracy" = correct / (correct + wrong + skipped) of an attempt, as a percentage. Raw marks are
    //     not comparable across tests with different totals and negative marking, so accuracy is used.
    //   * Papers: "uploaded" = Paper.CreatedAt, "published" = Paper.PublishedAt, "rejected" = audit entries
    //     "Paper.Reject" (a rejected paper returns to Draft and keeps no status of its own).
    //   * Question counts: PYP questions + active Question Bank questions that are not auto-mirrors of a PYP
    //     question (same rule as the original dashboard, so the total is not double counted).
    //
    // NOT AVAILABLE (no data source exists in SCORAM): payments, revenue, subscriptions, refunds,
    // notification delivery / failure / click tracking. They are returned as "unavailable" entries.
    public interface IAdminAnalyticsService
    {
        AnalyticsRange ResolveRange(string? from, string? to, int? tzOffsetMinutes, string? granularity);
        Task<AnalyticsOverviewDto> GetOverviewAsync(AnalyticsRange range, bool includeSensitive, CancellationToken ct = default);
        Task<AnalyticsUsersDto> GetUsersAsync(AnalyticsRange range, CancellationToken ct = default);
        Task<AnalyticsContentDto> GetContentAsync(AnalyticsRange range, CancellationToken ct = default);
        Task<AnalyticsLearningDto> GetLearningAsync(AnalyticsRange range, CancellationToken ct = default);
        Task<AnalyticsCommunityDto> GetCommunityAsync(AnalyticsRange range, CancellationToken ct = default);
        Task<AnalyticsOperationsDto> GetOperationsAsync(AnalyticsRange range, CancellationToken ct = default);

        /// <summary>Makes the next read recompute instead of using the 60-second cache (the "Refresh" button).</summary>
        void InvalidateCache();
    }

    public sealed class AnalyticsRange
    {
        public DateOnly FromLocal { get; init; }
        public DateOnly ToLocal { get; init; }               // inclusive
        public DateOnly PrevFromLocal { get; init; }
        public DateOnly PrevToLocal { get; init; }           // inclusive
        public DateTime FromUtc { get; init; }
        public DateTime ToUtc { get; init; }                 // exclusive
        public DateTime PrevFromUtc { get; init; }
        public DateTime PrevToUtc { get; init; }             // exclusive
        public int OffsetMinutes { get; init; }
        public int Days { get; init; }
        public string Granularity { get; init; } = "day";
        public DateTime NowUtc { get; init; }

        public DateTime LocalMidnightToUtc(DateOnly localDate) =>
            DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue).AddMinutes(-OffsetMinutes), DateTimeKind.Utc);

        public DateOnly TodayLocal => DateOnly.FromDateTime(NowUtc.AddMinutes(OffsetMinutes));
    }

    public sealed class AnalyticsBucket
    {
        public DateOnly Start { get; init; }
        public DateOnly End { get; init; }                   // inclusive
        public string Label { get; init; } = string.Empty;
    }

    public class AdminAnalyticsService : IAdminAnalyticsService
    {
        private const int MaxRangeDays = 366;
        private const int MaxScatterPoints = 400;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
        // Bumped by InvalidateCache(); part of every cache key, so old entries are simply never read again
        // and expire on their own within CacheTtl.
        private static int _cacheVersion;

        public void InvalidateCache() => Interlocked.Increment(ref _cacheVersion);

        private readonly ScoramDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly IInstantSearchService _search;

        public AdminAnalyticsService(ScoramDbContext db, IMemoryCache cache, IInstantSearchService search)
        {
            _db = db;
            _cache = cache;
            _search = search;
        }

        // ------------------------------------------------------------------------------------------
        // Range / bucket helpers
        // ------------------------------------------------------------------------------------------

        public AnalyticsRange ResolveRange(string? from, string? to, int? tzOffsetMinutes, string? granularity)
        {
            var offset = Math.Clamp(tzOffsetMinutes ?? 330, -840, 840);
            var nowUtc = DateTime.UtcNow;
            var todayLocal = DateOnly.FromDateTime(nowUtc.AddMinutes(offset));

            var toLocal = TryParseDate(to) ?? todayLocal;
            var fromLocal = TryParseDate(from) ?? toLocal.AddDays(-29);
            if (toLocal > todayLocal) toLocal = todayLocal;       // the future has no data
            if (fromLocal > toLocal) fromLocal = toLocal;
            if (toLocal.DayNumber - fromLocal.DayNumber + 1 > MaxRangeDays)
                fromLocal = toLocal.AddDays(-(MaxRangeDays - 1));

            var days = toLocal.DayNumber - fromLocal.DayNumber + 1;
            var prevToLocal = fromLocal.AddDays(-1);
            var prevFromLocal = prevToLocal.AddDays(-(days - 1));

            var g = (granularity ?? "day").Trim().ToLowerInvariant();
            if (g != "week" && g != "month") g = "day";
            // Keep charts readable: a very long range in daily buckets is auto-coarsened.
            if (g == "day" && days > 120) g = "week";

            DateTime Utc(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue).AddMinutes(-offset), DateTimeKind.Utc);

            return new AnalyticsRange
            {
                FromLocal = fromLocal,
                ToLocal = toLocal,
                PrevFromLocal = prevFromLocal,
                PrevToLocal = prevToLocal,
                FromUtc = Utc(fromLocal),
                ToUtc = Utc(toLocal.AddDays(1)),
                PrevFromUtc = Utc(prevFromLocal),
                PrevToUtc = Utc(prevToLocal.AddDays(1)),
                OffsetMinutes = offset,
                Days = days,
                Granularity = g,
                NowUtc = nowUtc
            };
        }

        private static DateOnly? TryParseDate(string? raw) =>
            DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        private static List<AnalyticsBucket> BuildBuckets(DateOnly from, DateOnly to, string granularity)
        {
            var buckets = new List<AnalyticsBucket>();
            var cursor = from;
            while (cursor <= to)
            {
                DateOnly end;
                string label;
                switch (granularity)
                {
                    case "week":
                    {
                        // Monday-start weeks, clipped to the requested range.
                        var daysFromMonday = ((int)cursor.DayOfWeek + 6) % 7;
                        end = cursor.AddDays(6 - daysFromMonday);
                        label = "Wk " + cursor.ToString("d MMM", CultureInfo.InvariantCulture);
                        break;
                    }
                    case "month":
                    {
                        end = new DateOnly(cursor.Year, cursor.Month, 1).AddMonths(1).AddDays(-1);
                        label = cursor.ToString("MMM yyyy", CultureInfo.InvariantCulture);
                        break;
                    }
                    default:
                        end = cursor;
                        label = cursor.ToString("d MMM", CultureInfo.InvariantCulture);
                        break;
                }
                if (end > to) end = to;
                buckets.Add(new AnalyticsBucket { Start = cursor, End = end, Label = label });
                cursor = end.AddDays(1);
            }
            return buckets;
        }

        private static int SumDays(Dictionary<DateOnly, int> perDay, AnalyticsBucket b)
        {
            var total = 0;
            for (var d = b.Start; d <= b.End; d = d.AddDays(1))
                if (perDay.TryGetValue(d, out var n)) total += n;
            return total;
        }

        private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static AnalyticsRangeDto ToDto(AnalyticsRange r) => new()
        {
            From = Iso(r.FromLocal),
            To = Iso(r.ToLocal),
            PreviousFrom = Iso(r.PrevFromLocal),
            PreviousTo = Iso(r.PrevToLocal),
            Days = r.Days,
            Granularity = r.Granularity,
            TzOffsetMinutes = r.OffsetMinutes,
            GeneratedAt = DateTime.UtcNow
        };

        private Task<T> Cached<T>(string name, AnalyticsRange r, string extra, Func<Task<T>> factory)
        {
            var key = $"analytics:v{_cacheVersion}:{name}:{Iso(r.FromLocal)}:{Iso(r.ToLocal)}:{r.OffsetMinutes}:{r.Granularity}:{extra}";
            return _cache.GetOrCreateAsync(key, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheTtl;
                return factory();
            })!;
        }

        // ------------------------------------------------------------------------------------------
        // Daily count queries (grouped in SQL by the admin-local calendar date)
        // ------------------------------------------------------------------------------------------

        private async Task<Dictionary<DateOnly, int>> DailyUsersAsync(AnalyticsRange r, CancellationToken ct)
        {
            double off = r.OffsetMinutes;
            var rows = await _db.Users
                .Where(u => u.CreatedAt >= r.FromUtc && u.CreatedAt < r.ToUtc)
                .GroupBy(u => u.CreatedAt.AddMinutes(off).Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            return rows.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count);
        }

        private async Task<Dictionary<DateOnly, int>> DailyPapersUploadedAsync(DateTime fromUtc, DateTime toUtc, double off, CancellationToken ct)
        {
            var rows = await _db.Papers
                .Where(p => p.CreatedAt >= fromUtc && p.CreatedAt < toUtc)
                .GroupBy(p => p.CreatedAt.AddMinutes(off).Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            return rows.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count);
        }

        private async Task<Dictionary<DateOnly, int>> DailyPapersPublishedAsync(AnalyticsRange r, CancellationToken ct)
        {
            double off = r.OffsetMinutes;
            var rows = await _db.Papers
                .Where(p => p.PublishedAt != null && p.PublishedAt >= r.FromUtc && p.PublishedAt < r.ToUtc)
                .GroupBy(p => p.PublishedAt!.Value.AddMinutes(off).Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            return rows.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count);
        }

        private async Task<Dictionary<DateOnly, int>> DailyAuditAsync(AnalyticsRange r, string action, CancellationToken ct)
        {
            double off = r.OffsetMinutes;
            var rows = await _db.AuditLogs
                .Where(a => a.Action == action && a.CreatedAt >= r.FromUtc && a.CreatedAt < r.ToUtc)
                .GroupBy(a => a.CreatedAt.AddMinutes(off).Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            return rows.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count);
        }

        private async Task<Dictionary<DateOnly, int>> DailyAttemptsStartedAsync(AnalyticsRange r, CancellationToken ct)
        {
            double off = r.OffsetMinutes;
            var rows = await _db.StudentTestResults
                .Where(a => a.StartedAt >= r.FromUtc && a.StartedAt < r.ToUtc)
                .GroupBy(a => a.StartedAt.AddMinutes(off).Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            return rows.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count);
        }

        private static List<decimal> Spark(Dictionary<DateOnly, int> perDay, AnalyticsRange r)
        {
            var granularity = r.Days <= 31 ? "day" : "week";
            return BuildBuckets(r.FromLocal, r.ToLocal, granularity).Select(b => (decimal)SumDays(perDay, b)).ToList();
        }

        private static KpiDto Kpi(string key, string group, string label, string definition, decimal? value,
            string unit = "count", string? drill = null, decimal? previous = null, string polarity = "neutral",
            List<decimal>? spark = null)
        {
            decimal? delta = null, pct = null;
            if (value.HasValue && previous.HasValue)
            {
                delta = value.Value - previous.Value;
                if (previous.Value != 0) pct = Math.Round(delta.Value / previous.Value * 100m, 1);
            }
            return new KpiDto
            {
                Key = key, Group = group, Label = label, Definition = definition, Unit = unit, DrillTo = drill,
                Value = value, Previous = previous, Delta = delta, DeltaPct = pct, Polarity = polarity, Spark = spark
            };
        }

        // ------------------------------------------------------------------------------------------
        // Overview (dashboard KPIs)
        // ------------------------------------------------------------------------------------------

        public Task<AnalyticsOverviewDto> GetOverviewAsync(AnalyticsRange r, bool includeSensitive, CancellationToken ct = default) =>
            Cached("overview", r, includeSensitive ? "full" : "basic", () => BuildOverviewAsync(r, includeSensitive, ct));

        private async Task<AnalyticsOverviewDto> BuildOverviewAsync(AnalyticsRange r, bool includeSensitive, CancellationToken ct)
        {
            var kpis = new List<KpiDto>();
            double off = r.OffsetMinutes;
            var todayStartUtc = r.LocalMidnightToUtc(r.TodayLocal);

            // ---- Content operations (visible to every admin, same as the original dashboard) ----
            var totalPyp = await _db.Questions.CountAsync(ct);
            var totalPyq = await _db.QuestionBankQuestions
                .CountAsync(x => x.IsActive && !_db.Questions.Any(q => q.MirroredToQuestionBankQuestionId == x.Id), ct);

            kpis.Add(Kpi("content.questions", "Content", "Total questions",
                "PYP questions plus active Question Bank questions (auto-mirrored copies of PYP questions are counted once).",
                totalPyp + totalPyq, drill: "/admin/question-bank"));
            kpis.Add(Kpi("content.papers", "Content", "Total papers", "All papers in any status.",
                await _db.Papers.CountAsync(ct), drill: "/admin/papers"));
            kpis.Add(Kpi("content.exams", "Content", "Total exams", "All exams, including blocked ones.",
                await _db.Exams.CountAsync(ct), drill: "/admin/exams"));
            kpis.Add(Kpi("content.publishedPapers", "Content", "Published papers", "Papers visible to students right now.",
                await _db.Papers.CountAsync(p => p.Status == PaperStatus.Published, ct), drill: "/admin/papers?status=Published"));
            kpis.Add(Kpi("content.draftPapers", "Content", "Draft papers", "Papers still being prepared, not yet submitted.",
                await _db.Papers.CountAsync(p => p.Status == PaperStatus.Draft, ct), drill: "/admin/papers?status=Draft"));
            kpis.Add(Kpi("content.pendingReview", "Content", "Pending review", "Papers submitted and waiting for a publisher.",
                await _db.Papers.CountAsync(p => p.Status == PaperStatus.PendingReview, ct), drill: "/admin/review", polarity: "down_good"));
            kpis.Add(Kpi("content.mockTests", "Content", "Mock tests", "All mock tests in any status.",
                await _db.MockTests.CountAsync(ct), drill: "/admin/mock-tests"));

            var uploadedNow = await _db.Papers.CountAsync(p => p.CreatedAt >= r.FromUtc && p.CreatedAt < r.ToUtc, ct);
            var uploadedPrev = await _db.Papers.CountAsync(p => p.CreatedAt >= r.PrevFromUtc && p.CreatedAt < r.PrevToUtc, ct);
            var uploadsDaily = await DailyPapersUploadedAsync(r.FromUtc, r.ToUtc, off, ct);
            kpis.Add(Kpi("content.papersUploaded", "Content", "Papers uploaded", "Papers created during the selected period.",
                uploadedNow, drill: "/admin/papers", previous: uploadedPrev, polarity: "up_good", spark: Spark(uploadsDaily, r)));

            var publishedNow = await _db.Papers.CountAsync(p => p.PublishedAt != null && p.PublishedAt >= r.FromUtc && p.PublishedAt < r.ToUtc, ct);
            var publishedPrev = await _db.Papers.CountAsync(p => p.PublishedAt != null && p.PublishedAt >= r.PrevFromUtc && p.PublishedAt < r.PrevToUtc, ct);
            kpis.Add(Kpi("content.papersPublished", "Content", "Papers published", "Papers whose publish time falls in the selected period.",
                publishedNow, drill: "/admin/papers?status=Published", previous: publishedPrev, polarity: "up_good"));

            async Task<int> QuestionsAddedAsync(DateTime a, DateTime b) =>
                await _db.Questions.CountAsync(q => q.CreatedAt >= a && q.CreatedAt < b, ct)
                + await _db.QuestionBankQuestions.CountAsync(x => x.IsActive && x.CreatedAt >= a && x.CreatedAt < b
                    && !_db.Questions.Any(q => q.MirroredToQuestionBankQuestionId == x.Id), ct);
            kpis.Add(Kpi("content.questionsAdded", "Content", "Questions added", "New PYP and Question Bank questions created during the selected period.",
                await QuestionsAddedAsync(r.FromUtc, r.ToUtc), drill: "/admin/question-bank",
                previous: await QuestionsAddedAsync(r.PrevFromUtc, r.PrevToUtc), polarity: "up_good"));

            // ---- Moderation backlog (point-in-time) ----
            kpis.Add(Kpi("moderation.questionReports", "Moderation", "Pending question reports", "Question reports not yet resolved or rejected.",
                await _db.QuestionReports.CountAsync(x => x.Status == ReportStatus.Pending, ct), drill: "/admin/question-bank/reports", polarity: "down_good"));
            kpis.Add(Kpi("moderation.chatReports", "Moderation", "Pending chat reports", "Reported chat messages awaiting action.",
                await _db.ChatReports.CountAsync(x => x.Status == ChatReportStatus.Pending, ct), drill: "/admin/chat", polarity: "down_good"));
            kpis.Add(Kpi("moderation.commentReports", "Moderation", "Pending comment reports", "Reported discussion comments awaiting action.",
                await _db.CommentReports.CountAsync(x => x.Status == ReportStatus.Pending, ct), drill: "/admin/comment-reports", polarity: "down_good"));
            kpis.Add(Kpi("moderation.solutions", "Moderation", "Pending solutions", "Student-submitted solutions not yet approved.",
                await _db.QuestionSolutions.CountAsync(x => !x.IsApproved, ct), drill: "/admin/solutions", polarity: "down_good"));

            // ---- Operations ----
            kpis.Add(Kpi("operations.admins", "Operations", "Total admins", "Admin accounts (active and deactivated).",
                await _db.Admins.CountAsync(ct), drill: "/admin/admins"));
            kpis.Add(Kpi("operations.openTickets", "Operations", "Open support tickets", "Feedback items with status New or In Review.",
                await _db.UserFeedbacks.CountAsync(f => f.Status == FeedbackStatus.New || f.Status == FeedbackStatus.InReview, ct),
                drill: "/admin/support", polarity: "down_good"));

            var dbHealthy = false;
            try { dbHealthy = await _db.Database.CanConnectAsync(ct); } catch { /* reported as unhealthy */ }
            kpis.Add(Kpi("operations.database", "Operations", "Database", "Result of a live connection check made just now (1 = reachable).",
                dbHealthy ? 1 : 0, drill: "/admin/system-health"));
            kpis.Add(Kpi("operations.searchIndex", "Operations", "Search index", "Result of a live health check against the search service (1 = healthy).",
                await _search.IsHealthyAsync() ? 1 : 0, drill: "/admin/system-health"));

            // ---- Sensitive sections: need ViewAnalytics ----
            if (includeSensitive)
            {
                var totalUsers = await _db.Users.CountAsync(ct);
                var newUsersNow = await _db.Users.CountAsync(u => u.CreatedAt >= r.FromUtc && u.CreatedAt < r.ToUtc, ct);
                var newUsersPrev = await _db.Users.CountAsync(u => u.CreatedAt >= r.PrevFromUtc && u.CreatedAt < r.PrevToUtc, ct);
                var usersDaily = await DailyUsersAsync(r, ct);

                kpis.Add(Kpi("users.total", "Users", "Registered students", "All student accounts, active and suspended.",
                    totalUsers, drill: "/admin/users"));
                kpis.Add(Kpi("users.new", "Users", "New signups", "Student accounts created during the selected period.",
                    newUsersNow, drill: "/admin/users", previous: newUsersPrev, polarity: "up_good", spark: Spark(usersDaily, r)));
                kpis.Add(Kpi("users.activeToday", "Users", "Active today", "Students whose most recent login was today (admin-local day). Point-in-time: login history is not stored.",
                    await _db.Users.CountAsync(u => u.LastActiveAt != null && u.LastActiveAt >= todayStartUtc, ct), drill: "/admin/users"));
                kpis.Add(Kpi("users.active30d", "Users", "Active in last 30 days", "Students whose most recent login was within the last 30 days (a monthly-active-user count as of now).",
                    await _db.Users.CountAsync(u => u.LastActiveAt != null && u.LastActiveAt >= r.NowUtc.AddDays(-30), ct), drill: "/admin/users"));
                kpis.Add(Kpi("users.suspended", "Users", "Suspended accounts", "Students currently blocked from signing in.",
                    await _db.Users.CountAsync(u => !u.IsActive, ct), drill: "/admin/users?status=suspended"));

                // ---- Learning activity ----
                var attemptsNow = _db.StudentTestResults.Where(a => a.StartedAt >= r.FromUtc && a.StartedAt < r.ToUtc);
                var attemptsPrev = _db.StudentTestResults.Where(a => a.StartedAt >= r.PrevFromUtc && a.StartedAt < r.PrevToUtc);

                async Task<(int started, int completed, int learners, double? accuracy)> LearnAsync(IQueryable<Models.StudentTestResult> q)
                {
                    var started = await q.CountAsync(ct);
                    var completed = await q.CountAsync(a => a.Status == TestAttemptStatus.Submitted || a.Status == TestAttemptStatus.AutoSubmitted, ct);
                    var learners = await q.Select(a => a.UserId).Distinct().CountAsync(ct);
                    var accuracy = await q
                        .Where(a => (a.Status == TestAttemptStatus.Submitted || a.Status == TestAttemptStatus.AutoSubmitted)
                                    && (a.CorrectCount + a.WrongCount + a.SkippedCount) > 0)
                        .Select(a => (double?)(a.CorrectCount * 100.0 / (a.CorrectCount + a.WrongCount + a.SkippedCount)))
                        .AverageAsync(ct);
                    return (started, completed, learners, accuracy);
                }

                var now = await LearnAsync(attemptsNow);
                var prev = await LearnAsync(attemptsPrev);
                var attemptsDaily = await DailyAttemptsStartedAsync(r, ct);

                kpis.Add(Kpi("learning.started", "Learning", "Test attempts", "Practice, mock, previous-year-paper and quiz attempts started during the period.",
                    now.started, previous: prev.started, polarity: "up_good", spark: Spark(attemptsDaily, r)));
                kpis.Add(Kpi("learning.completed", "Learning", "Completed tests", "Attempts started in the period that were submitted (manually or by the timer).",
                    now.completed, previous: prev.completed, polarity: "up_good"));
                kpis.Add(Kpi("learning.learners", "Learning", "Active learners", "Distinct students who started at least one test attempt during the period.",
                    now.learners, previous: prev.learners, polarity: "up_good"));
                kpis.Add(Kpi("learning.completionRate", "Learning", "Test completion rate", "Completed attempts divided by attempts started, for attempts started in the period.",
                    now.started == 0 ? null : Math.Round(now.completed * 100m / now.started, 1), unit: "percent",
                    previous: prev.started == 0 ? null : Math.Round(prev.completed * 100m / prev.started, 1), polarity: "up_good"));
                kpis.Add(Kpi("learning.avgAccuracy", "Learning", "Average accuracy", "Mean of correct / (correct + wrong + skipped) over completed attempts started in the period.",
                    now.accuracy == null ? null : Math.Round((decimal)now.accuracy.Value, 1), unit: "percent",
                    previous: prev.accuracy == null ? null : Math.Round((decimal)prev.accuracy.Value, 1), polarity: "up_good"));
            }

            return new AnalyticsOverviewDto
            {
                Range = ToDto(r),
                Kpis = kpis,
                Restricted = !includeSensitive,
                Unavailable = UnavailableBusinessMetrics(),
                UploadsLast7Days = await UploadsWindowAsync(r, 7, "day", ct),
                UploadsLast6Months = await UploadsWindowAsync(r, 6, "month", ct)
            };
        }

        private static List<UnavailableMetricDto> UnavailableBusinessMetrics() => new()
        {
            new() { Key = "business.revenue", Label = "Revenue, payments, subscriptions, refunds",
                Reason = "SCORAM has no paid access, payment provider or subscription data today, so there is nothing to measure. These cards appear once a purchase flow exists." },
            new() { Key = "notifications.delivery", Label = "Notification delivery, failures and clicks",
                Reason = "Notifications are stored per recipient, but push delivery results and clicks are not recorded. Only recipients and in-app reads are measurable." },
            new() { Key = "users.dau.history", Label = "Historical daily/monthly active users from logins",
                Reason = "Only each student's latest login time is stored, not a login history. Active learners (students who started a test) is shown instead." }
        };

        // Fixed windows for the two upload charts: last 7 days / last 6 months ending today (admin-local),
        // independent of the page's date filter -- same meaning as the original dashboard, but on correct
        // local-calendar boundaries.
        private async Task<List<BucketPointDto>> UploadsWindowAsync(AnalyticsRange r, int count, string granularity, CancellationToken ct)
        {
            DateOnly from, to = r.TodayLocal;
            from = granularity == "day"
                ? to.AddDays(-(count - 1))
                : new DateOnly(to.Year, to.Month, 1).AddMonths(-(count - 1));

            double off = r.OffsetMinutes;
            var fromUtc = r.LocalMidnightToUtc(from);
            var toUtc = r.LocalMidnightToUtc(to.AddDays(1));
            var perDay = await DailyPapersUploadedAsync(fromUtc, toUtc, off, ct);

            return BuildBuckets(from, to, granularity)
                .Select(b => new BucketPointDto
                {
                    Start = Iso(b.Start),
                    Label = granularity == "day" ? b.Start.ToString("ddd d MMM", CultureInfo.InvariantCulture) : b.Label,
                    Count = SumDays(perDay, b)
                }).ToList();
        }

        // ------------------------------------------------------------------------------------------
        // Users tab
        // ------------------------------------------------------------------------------------------

        public Task<AnalyticsUsersDto> GetUsersAsync(AnalyticsRange r, CancellationToken ct = default) =>
            Cached("users", r, "-", () => BuildUsersAsync(r, ct));

        private async Task<AnalyticsUsersDto> BuildUsersAsync(AnalyticsRange r, CancellationToken ct)
        {
            double off = r.OffsetMinutes;
            var usersDaily = await DailyUsersAsync(r, ct);
            var usersBefore = await _db.Users.CountAsync(u => u.CreatedAt < r.FromUtc, ct);
            var buckets = BuildBuckets(r.FromLocal, r.ToLocal, r.Granularity);

            // Active learners = DISTINCT students per bucket. A daily grouped query can serve day buckets
            // directly; for week/month buckets distinct counts cannot be summed from daily numbers, so each
            // bucket gets its own COUNT(DISTINCT) query (at most ~53 buckets).
            var activity = new Dictionary<DateOnly, (int learners, int attempts)>();
            if (r.Granularity == "day")
            {
                var rows = await _db.StudentTestResults
                    .Where(a => a.StartedAt >= r.FromUtc && a.StartedAt < r.ToUtc)
                    .GroupBy(a => a.StartedAt.AddMinutes(off).Date)
                    .Select(g => new { Day = g.Key, Learners = g.Select(x => x.UserId).Distinct().Count(), Attempts = g.Count() })
                    .ToListAsync(ct);
                foreach (var row in rows) activity[DateOnly.FromDateTime(row.Day)] = (row.Learners, row.Attempts);
            }
            else
            {
                foreach (var b in buckets)
                {
                    var a = r.LocalMidnightToUtc(b.Start);
                    var z = r.LocalMidnightToUtc(b.End.AddDays(1));
                    var q = _db.StudentTestResults.Where(x => x.StartedAt >= a && x.StartedAt < z);
                    activity[b.Start] = (await q.Select(x => x.UserId).Distinct().CountAsync(ct), await q.CountAsync(ct));
                }
            }

            var series = new List<UserSeriesPointDto>();
            var running = usersBefore;
            foreach (var b in buckets)
            {
                var added = SumDays(usersDaily, b);
                running += added;
                activity.TryGetValue(b.Start, out var act);
                series.Add(new UserSeriesPointDto
                {
                    Start = Iso(b.Start), Label = b.Label, NewUsers = added, CumulativeUsers = running,
                    ActiveLearners = act.learners, TestAttempts = act.attempts
                });
            }

            return new AnalyticsUsersDto
            {
                Range = ToDto(r),
                Series = series,
                UsersBeforeRange = usersBefore,
                Notes = new List<string>
                {
                    "Cumulative users = accounts created up to the end of each bucket. Accounts deleted by students are no longer in the database, so very old totals can read slightly high.",
                    "Active learners are distinct students who started a test in the bucket; test attempts are events (one student can start many). Login history is not stored, so a login-based DAU/MAU trend is not available."
                }
            };
        }

        // ------------------------------------------------------------------------------------------
        // Content tab
        // ------------------------------------------------------------------------------------------

        public Task<AnalyticsContentDto> GetContentAsync(AnalyticsRange r, CancellationToken ct = default) =>
            Cached("content", r, "-", () => BuildContentAsync(r, ct));

        private async Task<AnalyticsContentDto> BuildContentAsync(AnalyticsRange r, CancellationToken ct)
        {
            double off = r.OffsetMinutes;
            var uploaded = await DailyPapersUploadedAsync(r.FromUtc, r.ToUtc, off, ct);
            var published = await DailyPapersPublishedAsync(r, ct);
            var rejected = await DailyAuditAsync(r, "Paper.Reject", ct);

            var publishing = BuildBuckets(r.FromLocal, r.ToLocal, r.Granularity)
                .Select(b => new PublishingPointDto
                {
                    Start = Iso(b.Start), Label = b.Label,
                    Uploaded = SumDays(uploaded, b), Published = SumDays(published, b), Rejected = SumDays(rejected, b)
                }).ToList();

            // Subject distribution: active Question Bank questions per subject (the bank is the master list
            // every filter is built from). Top 7 subjects, the rest grouped as "Other".
            var subjectRows = await _db.QuestionBankQuestions
                .Where(q => q.IsActive)
                .GroupBy(q => q.Subject!.Name)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync(ct);

            // Exam distribution: papers per exam.
            var examRows = await _db.Papers
                .GroupBy(p => p.Exam!.Name)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync(ct);

            // Organizations: exams, published papers, and learners (distinct students who selected one of
            // the organization's exams in My Exams). SCORAM has no organization-scoped admin role, so
            // access follows the admin panel's normal permissions.
            var orgBase = await _db.Organizations
                .Select(o => new { o.Id, o.Name, Exams = o.Exams.Count() })
                .ToListAsync(ct);
            var publishedByOrg = await _db.Papers
                .Where(p => p.Status == PaperStatus.Published && p.Exam!.OrganizationId != null)
                .GroupBy(p => p.Exam!.OrganizationId)
                .Select(g => new { OrgId = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            var learnersByOrg = await _db.UserExamPreferences
                .Where(p => p.Exam!.OrganizationId != null)
                .GroupBy(p => p.Exam!.OrganizationId)
                .Select(g => new { OrgId = g.Key, Learners = g.Select(x => x.UserId).Distinct().Count() })
                .ToListAsync(ct);
            var publishedMap = publishedByOrg.Where(x => x.OrgId != null).ToDictionary(x => x.OrgId!.Value, x => x.Count);
            var learnerMap = learnersByOrg.Where(x => x.OrgId != null).ToDictionary(x => x.OrgId!.Value, x => x.Learners);

            var orgs = orgBase
                .Select(o => new OrganizationPerformanceDto
                {
                    Name = o.Name,
                    Exams = o.Exams,
                    PublishedPapers = publishedMap.TryGetValue(o.Id, out var pub) ? pub : 0,
                    Learners = learnerMap.TryGetValue(o.Id, out var l) ? l : 0
                })
                .OrderByDescending(o => o.Learners).ThenByDescending(o => o.PublishedPapers)
                .Take(12).ToList();

            return new AnalyticsContentDto
            {
                Range = ToDto(r),
                Publishing = publishing,
                SubjectDistribution = ToSlices(subjectRows.Select(x => (x.Name, x.Count)).ToList(), 7),
                ExamDistribution = ToSlices(examRows.Select(x => (x.Name, x.Count)).ToList(), 7),
                Organizations = orgs,
                Notes = new List<string>
                {
                    "Rejected counts come from audit entries: a rejected paper goes back to Draft and does not keep a Rejected status.",
                    "Subject and exam distributions are all-time snapshots, not filtered by the date range."
                }
            };
        }

        private static List<DistributionSliceDto> ToSlices(List<(string Name, int Count)> rows, int keep)
        {
            var total = rows.Sum(x => x.Count);
            if (total == 0) return new List<DistributionSliceDto>();

            var top = rows.Take(keep).ToList();
            var otherCount = rows.Skip(keep).Sum(x => x.Count);
            if (otherCount > 0) top.Add(("Other", otherCount));

            return top.Select(x => new DistributionSliceDto
            {
                Name = string.IsNullOrWhiteSpace(x.Name) ? "Unspecified" : x.Name,
                Count = x.Count,
                Percent = Math.Round(x.Count * 100.0 / total, 1)
            }).ToList();
        }

        // ------------------------------------------------------------------------------------------
        // Learning tab
        // ------------------------------------------------------------------------------------------

        public Task<AnalyticsLearningDto> GetLearningAsync(AnalyticsRange r, CancellationToken ct = default) =>
            Cached("learning", r, "-", () => BuildLearningAsync(r, ct));

        private async Task<AnalyticsLearningDto> BuildLearningAsync(AnalyticsRange r, CancellationToken ct)
        {
            double off = r.OffsetMinutes;

            var rows = await _db.StudentTestResults
                .Where(a => a.StartedAt >= r.FromUtc && a.StartedAt < r.ToUtc)
                .GroupBy(a => new { Day = a.StartedAt.AddMinutes(off).Date, a.Status })
                .Select(g => new { g.Key.Day, g.Key.Status, Count = g.Count() })
                .ToListAsync(ct);

            Dictionary<DateOnly, int> Pick(Func<TestAttemptStatus, bool> predicate) => rows
                .Where(x => predicate(x.Status))
                .GroupBy(x => DateOnly.FromDateTime(x.Day))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

            var all = Pick(_ => true);
            var completed = Pick(s => s == TestAttemptStatus.Submitted || s == TestAttemptStatus.AutoSubmitted);
            var abandoned = Pick(s => s == TestAttemptStatus.Expired);
            var inProgress = Pick(s => s == TestAttemptStatus.InProgress);

            var activity = BuildBuckets(r.FromLocal, r.ToLocal, r.Granularity)
                .Select(b => new TestActivityPointDto
                {
                    Start = Iso(b.Start), Label = b.Label,
                    Started = SumDays(all, b), Completed = SumDays(completed, b),
                    Abandoned = SumDays(abandoned, b), InProgress = SumDays(inProgress, b)
                }).ToList();

            // Scatter: each dot is ONE completed attempt (x = time taken in minutes, y = accuracy %). Capped at
            // the most recent 400 so the chart stays responsive; the total eligible count is returned so the
            // UI can say it is a sample.
            var eligible = _db.StudentTestResults.Where(a =>
                a.StartedAt >= r.FromUtc && a.StartedAt < r.ToUtc
                && (a.Status == TestAttemptStatus.Submitted || a.Status == TestAttemptStatus.AutoSubmitted)
                && a.TimeTakenSeconds > 0
                && (a.CorrectCount + a.WrongCount + a.SkippedCount) > 0);

            var eligibleCount = await eligible.CountAsync(ct);
            var sample = await eligible.OrderByDescending(a => a.StartedAt).Take(MaxScatterPoints)
                .Select(a => new { a.TimeTakenSeconds, a.CorrectCount, a.WrongCount, a.SkippedCount, a.TestKind })
                .ToListAsync(ct);
            var scatter = sample.Select(a => new ScatterPointDto
            {
                Minutes = Math.Round(a.TimeTakenSeconds / 60.0, 1),
                Accuracy = Math.Round(a.CorrectCount * 100.0 / (a.CorrectCount + a.WrongCount + a.SkippedCount), 1),
                Kind = a.TestKind.ToString()
            }).ToList();

            var kindCounts = await _db.StudentTestResults
                .Where(a => a.StartedAt >= r.FromUtc && a.StartedAt < r.ToUtc)
                .GroupBy(a => a.TestKind)
                .Select(g => new
                {
                    Kind = g.Key,
                    Attempts = g.Count(),
                    Completed = g.Count(a => a.Status == TestAttemptStatus.Submitted || a.Status == TestAttemptStatus.AutoSubmitted)
                })
                .ToListAsync(ct);
            var kindAccuracy = await _db.StudentTestResults
                .Where(a => a.StartedAt >= r.FromUtc && a.StartedAt < r.ToUtc
                            && (a.Status == TestAttemptStatus.Submitted || a.Status == TestAttemptStatus.AutoSubmitted)
                            && (a.CorrectCount + a.WrongCount + a.SkippedCount) > 0)
                .GroupBy(a => a.TestKind)
                .Select(g => new
                {
                    Kind = g.Key,
                    Accuracy = g.Average(a => a.CorrectCount * 100.0 / (a.CorrectCount + a.WrongCount + a.SkippedCount))
                })
                .ToListAsync(ct);
            var accuracyMap = kindAccuracy.ToDictionary(x => x.Kind, x => x.Accuracy);

            return new AnalyticsLearningDto
            {
                Range = ToDto(r),
                Activity = activity,
                Scatter = scatter,
                ScatterTotalEligible = eligibleCount,
                ByKind = kindCounts.Select(k => new TestKindSummaryDto
                {
                    Kind = k.Kind.ToString(), Attempts = k.Attempts, Completed = k.Completed,
                    AvgAccuracy = accuracyMap.TryGetValue(k.Kind, out var acc) ? Math.Round(acc, 1) : null
                }).OrderByDescending(k => k.Attempts).ToList(),
                Notes = new List<string>
                {
                    "Attempts are grouped by the day they were started. Completed = submitted or auto-submitted; Abandoned = expired; In progress = still open.",
                    "The scatter plot shows individual completed attempts (most recent 400 in the range); it is a sample when more exist."
                }
            };
        }

        // ------------------------------------------------------------------------------------------
        // Community / quality tab
        // ------------------------------------------------------------------------------------------

        public Task<AnalyticsCommunityDto> GetCommunityAsync(AnalyticsRange r, CancellationToken ct = default) =>
            Cached("community", r, "-", () => BuildCommunityAsync(r, ct));

        private async Task<AnalyticsCommunityDto> BuildCommunityAsync(AnalyticsRange r, CancellationToken ct)
        {
            double off = r.OffsetMinutes;

            var filed = await _db.QuestionReports
                .Where(x => x.CreatedAt >= r.FromUtc && x.CreatedAt < r.ToUtc)
                .GroupBy(x => x.CreatedAt.AddMinutes(off).Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            var filedMap = filed.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count);

            var resolvedRows = await _db.QuestionReports
                .Where(x => x.ResolvedAt != null && x.ResolvedAt >= r.FromUtc && x.ResolvedAt < r.ToUtc)
                .GroupBy(x => x.ResolvedAt!.Value.AddMinutes(off).Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            var resolvedMap = resolvedRows.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count);

            var trend = BuildBuckets(r.FromLocal, r.ToLocal, r.Granularity)
                .Select(b => new QualityPointDto
                {
                    Start = Iso(b.Start), Label = b.Label, Reported = SumDays(filedMap, b), Resolved = SumDays(resolvedMap, b)
                }).ToList();

            var qrStatus = await _db.QuestionReports
                .Where(x => x.CreatedAt >= r.FromUtc && x.CreatedAt < r.ToUtc)
                .GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            var chatStatus = await _db.ChatReports
                .Where(x => x.CreatedAt >= r.FromUtc && x.CreatedAt < r.ToUtc)
                .GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            var commentStatus = await _db.CommentReports
                .Where(x => x.CreatedAt >= r.FromUtc && x.CreatedAt < r.ToUtc)
                .GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            var feedbackStatus = await _db.UserFeedbacks
                .Where(x => x.CreatedAt >= r.FromUtc && x.CreatedAt < r.ToUtc)
                .GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

            var rejected = qrStatus.Where(x => x.Key == ReportStatus.Rejected).Sum(x => x.Count);
            var resolved = qrStatus.Where(x => x.Key == ReportStatus.Resolved).Sum(x => x.Count);
            double? rejectionRate = rejected + resolved == 0 ? null : Math.Round(rejected * 100.0 / (rejected + resolved), 1);

            return new AnalyticsCommunityDto
            {
                Range = ToDto(r),
                QuestionReportsTrend = trend,
                QuestionReportsByStatus = qrStatus.Select(x => new StatusCountDto { Status = x.Key.ToString(), Count = x.Count }).ToList(),
                ChatReportsByStatus = chatStatus.Select(x => new StatusCountDto { Status = x.Key.ToString(), Count = x.Count }).ToList(),
                CommentReportsByStatus = commentStatus.Select(x => new StatusCountDto { Status = x.Key.ToString(), Count = x.Count }).ToList(),
                FeedbackByStatus = feedbackStatus.Select(x => new StatusCountDto { Status = x.Key.ToString(), Count = x.Count }).ToList(),
                PendingSolutions = await _db.QuestionSolutions.CountAsync(x => !x.IsApproved, ct),
                QuestionReportRejectionRate = rejectionRate,
                Notes = new List<string>
                {
                    "Status breakdowns count reports FILED in the selected period by their current status. Resolved in the trend chart counts reports closed in the period regardless of when they were filed.",
                    "Rejection rate = rejected / (resolved + rejected) among question reports filed in the period."
                }
            };
        }

        // ------------------------------------------------------------------------------------------
        // Operations tab (requires Audit permission -- enforced by the controller)
        // ------------------------------------------------------------------------------------------

        public Task<AnalyticsOperationsDto> GetOperationsAsync(AnalyticsRange r, CancellationToken ct = default) =>
            Cached("operations", r, "-", () => BuildOperationsAsync(r, ct));

        private async Task<AnalyticsOperationsDto> BuildOperationsAsync(AnalyticsRange r, CancellationToken ct)
        {
            double off = r.OffsetMinutes;

            var rows = await _db.AuditLogs
                .Where(a => a.CreatedAt >= r.FromUtc && a.CreatedAt < r.ToUtc)
                .GroupBy(a => new { Day = a.CreatedAt.AddMinutes(off).Date, a.Action })
                .Select(g => new { g.Key.Day, g.Key.Action, Count = g.Count() })
                .ToListAsync(ct);

            // Module = the part of the action name before the first dot ("Paper.Publish" -> "Paper").
            static string ModuleOf(string action)
            {
                var dot = action.IndexOf('.');
                return dot > 0 ? action.Substring(0, dot) : action;
            }

            var perDayModule = rows
                .GroupBy(x => (Day: DateOnly.FromDateTime(x.Day), Module: ModuleOf(x.Action)))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

            var modules = perDayModule.GroupBy(kv => kv.Key.Module)
                .Select(g => new { Module = g.Key, Total = g.Sum(x => x.Value) })
                .OrderByDescending(x => x.Total).Select(x => x.Module).ToList();

            var activity = new List<AdminActivityPointDto>();
            foreach (var b in BuildBuckets(r.FromLocal, r.ToLocal, r.Granularity))
            {
                var point = new AdminActivityPointDto { Start = Iso(b.Start), Label = b.Label };
                foreach (var module in modules)
                {
                    var n = 0;
                    for (var d = b.Start; d <= b.End; d = d.AddDays(1))
                        if (perDayModule.TryGetValue((d, module), out var c)) n += c;
                    if (n > 0) point.ByModule[module] = n;
                    point.Total += n;
                }
                activity.Add(point);
            }

            var campaigns = await _db.Notifications
                .Where(n => n.Type == NotificationType.SystemAnnouncement && n.DedupKey != null
                            && n.CreatedAt >= r.FromUtc && n.CreatedAt < r.ToUtc)
                .GroupBy(n => n.DedupKey)
                .Select(g => new
                {
                    Key = g.Key,
                    Title = g.Min(n => n.Title),
                    SentAt = g.Min(n => n.CreatedAt),
                    Recipients = g.Count(),
                    Read = g.Count(n => n.IsRead)
                })
                .OrderByDescending(x => x.SentAt)
                .Take(10)
                .ToListAsync(ct);

            return new AnalyticsOperationsDto
            {
                Range = ToDto(r),
                AdminActivity = activity,
                Modules = modules,
                RecentCampaigns = campaigns.Select(c => new CampaignSummaryDto
                {
                    CampaignId = c.Key ?? string.Empty, Title = c.Title ?? string.Empty, SentAt = c.SentAt, Recipients = c.Recipients, Read = c.Read
                }).ToList(),
                Unavailable = new List<UnavailableMetricDto>
                {
                    new() { Key = "notifications.failed", Label = "Failed / delivered / clicked notifications",
                        Reason = "Push delivery results and click tracking are not stored. Recipients and in-app reads are shown instead." }
                },
                Notes = new List<string>
                {
                    "Admin activity counts audit-log entries by module (the part of the action name before the dot). Only actions that are audited appear here."
                }
            };
        }
    }
}
