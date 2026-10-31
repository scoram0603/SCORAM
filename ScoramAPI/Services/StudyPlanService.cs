using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Models;
using ScoramAPI.Services.Analytics;

namespace ScoramAPI.Services
{
    public sealed record StudyTaskUpdateOutcome(StudyTaskDto? Task, int StatusCode, string? Error);

    public interface IStudyPlanService
    {
        /// <summary>Today's (IST) plan: generated on first read, then idempotent. Progress is recomputed
        /// from real activity on every read.</summary>
        Task<StudyPlanDto> GetTodayAsync(Guid userId, IReadOnlyCollection<Guid>? examIds, CancellationToken ct);
        Task<StudyPlanHistoryDto> GetHistoryAsync(Guid userId, int days, CancellationToken ct);
        Task<StudyTaskUpdateOutcome> SetDismissedAsync(Guid userId, Guid taskId, bool dismissed, CancellationToken ct);
        /// <summary>Counts only (no generation) -- for the dashboard.</summary>
        Task<PlanSummaryDto> GetSummaryAsync(Guid userId, DateOnly day, CancellationToken ct);
        /// <summary>Weekly completion numbers a student has chosen to share with Study Partners.</summary>
        Task<StudyPlanShareSummaryDto> GetWeeklyShareSummaryAsync(Guid userId, CancellationToken ct);
    }

    /// <summary>
    /// SMART PROGRESS -- lightweight, rule-based daily plan built only from the student's recorded
    /// performance. Tasks have deterministic keys so generating a day twice never duplicates them;
    /// completion is DERIVED from real activity (revision answers, answered questions in submitted
    /// assessments, submitted assessments) and never from opening a page.
    /// </summary>
    public class StudyPlanService : IStudyPlanService
    {
        private readonly ScoramDbContext _db;
        private readonly IProgressAnalyticsService _analytics;
        private readonly IRevisionService _revision;
        private readonly AnalyticsOptions _o;
        private readonly IMemoryCache? _cache;
        private readonly TimeProvider _clock;

        // Process-wide (the service itself is scoped per request): concurrent generations for the same
        // student/day queue up instead of each running the heavy analysis.
        private static readonly KeyedAsyncLock GenerationLocks = new();

        // Loaded at most once per request; the taxonomy is admin-curated and not touched by student endpoints.
        private PracticeTargetResolver? _resolver;

        /// <param name="cache">Optional so existing call sites/tests keep compiling. Without it the
        /// "inputs unchanged" shortcut is simply never taken (the saturated-plan shortcut still is).</param>
        /// <param name="clock">Optional; defaults to the system clock. Lets tests drive the IST day.</param>
        public StudyPlanService(ScoramDbContext db, IProgressAnalyticsService analytics, IRevisionService revision,
            IOptions<AnalyticsOptions> options, IMemoryCache? cache = null, TimeProvider? clock = null)
        {
            _db = db;
            _analytics = analytics;
            _revision = revision;
            _o = options.Value;
            _cache = cache;
            _clock = clock ?? TimeProvider.System;
        }

        private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

        private async Task<PracticeTargetResolver> ResolverAsync(CancellationToken ct)
            => _resolver ??= await PracticeTargetResolver.LoadAsync(_db, ct);

        private static string PracticeKey(string subject, string topic) => $"practice:{subject}|{topic}";

        // ------------------------------------------------------------------ generation

        private async Task<PlanInputFingerprint> LoadFingerprintAsync(Guid userId, DateTime now, CancellationToken ct)
        {
            // Two small aggregate queries; the same GroupBy(1) shape the analytics code already uses.
            var attempts = await AttemptQueries.CompletedAttempts(_db, userId, null, null, null, null)
                .GroupBy(r => 1)
                .Select(g => new { Count = g.Count(), Last = g.Max(r => (DateTime?)r.AttemptedAt) })
                .FirstOrDefaultAsync(ct);

            // Every revision answer and every dismiss/restore writes UpdatedAt on the item (RevisionService),
            // and "due" can also flip purely with time, so the due count is part of the fingerprint.
            var items = await _db.MistakeRevisionItems.AsNoTracking()
                .Where(i => i.UserId == userId)
                .GroupBy(i => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    Last = g.Max(i => (DateTime?)i.UpdatedAt),
                    Due = g.Count(i => i.NextDueAt != null && i.NextDueAt <= now)
                })
                .FirstOrDefaultAsync(ct);

            return new PlanInputFingerprint(
                attempts?.Count ?? 0, attempts?.Last,
                items?.Count ?? 0, items?.Last, items?.Due ?? 0);
        }

        private PlanFreshnessEntry? TryGetFreshness(string cacheKey)
            => _cache != null && _cache.TryGetValue(cacheKey, out PlanFreshnessEntry? e) ? e : null;

        private void RememberFreshness(string cacheKey, PlanFreshnessEntry entry)
            => _cache?.Set(cacheKey, entry, StudyPlanFreshness.RecheckAfter);

        /// <summary>
        /// Makes sure today's plan has every task it should have, and returns the up-to-date tracked task list.
        /// Generation only ever ADDS missing task keys (never edits or removes a stored task), so skipping a pass
        /// is safe exactly when that pass could not add anything:
        ///  1. the plan is saturated (revise + timed present, practice slots full) -- exact, no data needed;
        ///  2. or nothing generation reads has changed since the last pass for this student/day/scope
        ///     (fingerprint equal, task count equal) and that pass is younger than RecheckAfter.
        /// Anything else -- a new IST day, a newly submitted test, a revision answer, a dismissed mistake, a
        /// scope change, a cold cache -- runs the full analysis exactly as before.
        /// </summary>
        private async Task<List<StudyPlanTask>> EnsureGeneratedAsync(
            Guid userId, DateOnly day, IReadOnlyCollection<Guid>? examIds, List<StudyPlanTask> tasks, CancellationToken ct)
        {
            if (StudyPlanFreshness.IsSaturated(tasks.Select(t => (t.TaskKey, t.TaskType)), _o.StudyPlanMaxPracticeTasks))
                return tasks;

            var cacheKey = StudyPlanFreshness.CacheKey(userId, day, examIds);
            // Captured BEFORE the analysis: data that changes while it runs makes the next read regenerate.
            var fingerprint = await LoadFingerprintAsync(userId, UtcNow, ct);
            if (StudyPlanFreshness.IsFresh(TryGetFreshness(cacheKey), fingerprint, tasks.Count))
                return tasks;

            bool clean;
            using (await GenerationLocks.AcquireAsync(StudyPlanFreshness.LockKey(userId, day), ct))
            {
                // Generation is deliberately NOT tied to the request's cancellation token. The first read of the
                // day does the heavy work; if the client gave up waiting (mobile timeout on a cold/busy server)
                // and that work were cancelled with it, the plan would never get saved and every retry would
                // start from zero. Letting it finish means the client's retry is answered from the saved tasks.
                // It is idempotent (unique key per user/day/task), so a duplicate or abandoned run is harmless.
                clean = await GenerateAsync(userId, day, examIds, fingerprint, cacheKey, CancellationToken.None);
            }

            // Whatever happened (we generated, a concurrent request did, or a unique-key conflict cleared the
            // tracker), re-read the day's tasks so the caller works on the persisted truth.
            _db.ChangeTracker.Clear();
            var reloaded = await _db.StudyPlanTasks.Where(t => t.UserId == userId && t.PlanDate == day).ToListAsync(ct);
            if (clean) RememberFreshness(cacheKey, new PlanFreshnessEntry(fingerprint, reloaded.Count));
            return reloaded;
        }

        /// <summary>Runs one generation pass under the per-student/day lock. Returns false only when a
        /// unique-key conflict could not be fully reconciled (the next read will simply try again).</summary>
        private async Task<bool> GenerateAsync(
            Guid userId, DateOnly day, IReadOnlyCollection<Guid>? examIds, PlanInputFingerprint fp, string cacheKey, CancellationToken ct)
        {
            // Re-read under the lock: the holder before us may just have saved the plan.
            var existing = await LoadExistingKeysAsync(userId, day, ct);
            if (StudyPlanFreshness.IsSaturated(existing, _o.StudyPlanMaxPracticeTasks)
                || StudyPlanFreshness.IsFresh(TryGetFreshness(cacheKey), fp, existing.Count))
                return true;

            var haveKeys = existing.Select(e => e.TaskKey).ToHashSet(StringComparer.Ordinal);
            var practiceCount = existing.Count(e => e.TaskType == StudyTaskType.PracticeTopic);

            var candidates = new List<StudyPlanTask>();
            var now = UtcNow;

            // 1. Revise due mistakes. (Only needs the Mistake Book if the task is still missing.)
            IReadOnlyList<MistakeState>? unscopedStates = null;
            if (!haveKeys.Contains("revise"))
            {
                unscopedStates = await _revision.GetMistakeStatesAsync(userId, new MistakeQuery(), ct);
                var open = unscopedStates.Where(s => s.Wrong > 0 && s.IsOpen).ToList();
                var due = open.Count(s => s.IsDue);
                if (due > 0)
                {
                    var repeated = open.Count(s => s.IsRepeated);
                    candidates.Add(new StudyPlanTask
                    {
                        UserId = userId,
                        PlanDate = day,
                        TaskKey = "revise",
                        TaskType = StudyTaskType.ReviseMistakes,
                        Title = $"Revise {Math.Min(_o.StudyPlanRevisionQuestions, due)} mistake{(Math.Min(_o.StudyPlanRevisionQuestions, due) == 1 ? "" : "s")} from your Mistake Book",
                        Reason = $"{due} of your {open.Count} open mistakes {(due == 1 ? "is" : "are")} due for revision"
                                 + (repeated > 0 ? $"; {repeated} {(repeated == 1 ? "has" : "have")} been wrong more than once." : "."),
                        TargetCount = Math.Min(_o.StudyPlanRevisionQuestions, due),
                        Priority = 1,
                        GeneratedAt = now
                    });
                }
            }

            // 2. Practise the weakest topics (never Unclassified, never topics without enough data).
            //    The topic analysis is the expensive part, so it only runs when a practice slot is still open.
            var slots = Math.Max(0, _o.StudyPlanMaxPracticeTasks - practiceCount);
            if (slots > 0)
            {
                var filter = new ProgressFilter { ExamIds = examIds is { Count: > 0 } ? examIds : null };
                // Mistake states and the taxonomy resolver are handed over so they are not queried a second time
                // (the analytics service only reuses the states when the filter narrows nothing).
                var insights = _analytics is IPreloadedTopicInsights preloadable
                    ? await preloadable.GetTopicInsightsAsync(userId, filter,
                        new TopicInsightPreload { UnscopedMistakeStates = unscopedStates, Resolver = await ResolverAsync(ct) }, ct)
                    : await _analytics.GetTopicInsightsAsync(userId, filter, ct);
                var weak = insights
                    .Where(t => !t.IsUnclassified && (t.Status == "NeedsAttention" || t.Status == "PracticeRecommended" || t.Status == "NeedsRetesting"))
                    .ToList();
                var priority = 2;
                foreach (var t in weak)
                {
                    if (slots <= 0) break;
                    var key = PracticeKey(t.Subject, t.Topic);
                    if (haveKeys.Contains(key)) continue;
                    var rec = t.Recommendations.FirstOrDefault(r => r.Code != "REVISE_MISTAKES") ?? t.Recommendations.FirstOrDefault();
                    candidates.Add(new StudyPlanTask
                    {
                        UserId = userId,
                        PlanDate = day,
                        TaskKey = key,
                        TaskType = StudyTaskType.PracticeTopic,
                        Title = $"Practise {_o.StudyPlanPracticeQuestions} questions: {t.Topic} ({t.Subject})",
                        Reason = rec?.Reason,
                        TargetCount = _o.StudyPlanPracticeQuestions,
                        SubjectName = t.Subject,
                        TopicName = t.Topic,
                        Priority = priority++,
                        GeneratedAt = now
                    });
                    slots--;
                }
            }

            // 3. One timed session, once the student has any completed history. The count and the latest
            //    attempt come from the fingerprint (same filtered query), so no extra round trips.
            if (fp.CompletedAttempts > 0 && !haveKeys.Contains("timed"))
            {
                var last = fp.LastAttemptAt;
                var lastDays = last.HasValue ? (int)Math.Floor((now - last.Value).TotalDays) : 0;
                candidates.Add(new StudyPlanTask
                {
                    UserId = userId,
                    PlanDate = day,
                    TaskKey = "timed",
                    TaskType = StudyTaskType.TimedSession,
                    Title = "Complete one timed practice session",
                    Reason = lastDays >= 1
                        ? $"Your last assessment was {lastDays} day{(lastDays == 1 ? "" : "s")} ago; a timed session keeps your pace and accuracy honest."
                        : "A timed session today keeps your pace and accuracy honest.",
                    TargetCount = 1,
                    Priority = 10,
                    GeneratedAt = now
                });
            }

            if (candidates.Count == 0) return true;
            return await SaveCandidatesAsync(userId, day, candidates, ct);
        }

        private async Task<List<(string TaskKey, StudyTaskType TaskType)>> LoadExistingKeysAsync(Guid userId, DateOnly day, CancellationToken ct)
        {
            var rows = await _db.StudyPlanTasks.AsNoTracking()
                .Where(t => t.UserId == userId && t.PlanDate == day)
                .Select(t => new { t.TaskKey, t.TaskType })
                .ToListAsync(ct);
            return rows.Select(r => (r.TaskKey, r.TaskType)).ToList();
        }

        /// <summary>Inserts only new rows. If the unique (user, day, key) index rejects the batch because a
        /// concurrent writer (another instance, or a request that raced past the lock) got there first, the
        /// whole batch was rolled back -- so insert just the keys that are still missing, once, without
        /// re-running any analysis. Existing rows are never touched.</summary>
        private async Task<bool> SaveCandidatesAsync(Guid userId, DateOnly day, List<StudyPlanTask> candidates, CancellationToken ct)
        {
            _db.StudyPlanTasks.AddRange(candidates);
            try
            {
                await _db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
            }

            var now = await LoadExistingKeysAsync(userId, day, ct);
            var haveKeys = now.Select(e => e.TaskKey).ToHashSet(StringComparer.Ordinal);
            var practice = now.Count(e => e.TaskType == StudyTaskType.PracticeTopic);
            var missing = new List<StudyPlanTask>();
            foreach (var c in candidates.OrderBy(c => c.Priority))
            {
                if (haveKeys.Contains(c.TaskKey)) continue;
                if (c.TaskType == StudyTaskType.PracticeTopic)
                {
                    if (practice >= _o.StudyPlanMaxPracticeTasks) continue; // slots were filled by the winner
                    practice++;
                }
                missing.Add(new StudyPlanTask
                {
                    UserId = c.UserId, PlanDate = c.PlanDate, TaskKey = c.TaskKey, TaskType = c.TaskType,
                    Title = c.Title, Reason = c.Reason, TargetCount = c.TargetCount,
                    SubjectName = c.SubjectName, TopicName = c.TopicName, Priority = c.Priority, GeneratedAt = c.GeneratedAt
                });
            }
            if (missing.Count == 0) return true; // the concurrent writer already stored everything we wanted

            _db.StudyPlanTasks.AddRange(missing);
            try
            {
                await _db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                return false; // unresolved: do not remember this pass as fresh, the next read retries
            }
        }

        // ------------------------------------------------------------------ live progress

        private async Task<(int Revision, int Timed, Dictionary<string, int> Practice)> LoadActivityAsync(
            Guid userId, DateOnly day, List<StudyPlanTask> tasks, CancellationToken ct)
        {
            var (start, end) = AnalyticsCalculator.DayBoundsUtc(day);

            var revision = await _db.RevisionAttempts.AsNoTracking()
                .CountAsync(r => r.UserId == userId && r.AnsweredAt >= start && r.AnsweredAt < end, ct);
            var timed = await AttemptQueries.CompletedAttempts(_db, userId, null, null, start, end).CountAsync(ct);

            var practice = new Dictionary<string, int>(StringComparer.Ordinal);
            var practiceTasks = tasks.Where(t => t.TaskType == StudyTaskType.PracticeTopic && t.SubjectName != null).ToList();
            if (practiceTasks.Count > 0)
            {
                var rows = await AttemptQueries.CompletedAnswers(_db, userId, null, null, start, end)
                    .Where(a => a.SelectedOption != null)
                    .GroupBy(a => new { a.SubjectSnapshot, a.TopicSnapshot })
                    .Select(g => new { g.Key.SubjectSnapshot, g.Key.TopicSnapshot, Count = g.Count() })
                    .ToListAsync(ct);
                foreach (var t in practiceTasks)
                {
                    var key = Taxonomy.TopicKey(t.SubjectName, t.TopicName);
                    practice[t.TaskKey] = rows.Where(r => Taxonomy.TopicKey(r.SubjectSnapshot, r.TopicSnapshot) == key).Sum(r => r.Count);
                }
            }
            return (revision, timed, practice);
        }

        private static int ProgressFor(StudyPlanTask t, (int Revision, int Timed, Dictionary<string, int> Practice) a) => t.TaskType switch
        {
            StudyTaskType.ReviseMistakes => a.Revision,
            StudyTaskType.TimedSession => a.Timed,
            _ => a.Practice.TryGetValue(t.TaskKey, out var n) ? n : 0
        };

        /// <summary>Refreshes progress for a day's tasks from real activity, persisting newly completed ones.</summary>
        private async Task RefreshProgressAsync(Guid userId, DateOnly day, List<StudyPlanTask> tasks, CancellationToken ct)
        {
            var activity = await LoadActivityAsync(userId, day, tasks, ct);
            var changed = false;
            foreach (var t in tasks)
            {
                if (t.Status == StudyTaskStatus.Dismissed) continue;
                var progress = Math.Min(ProgressFor(t, activity), t.TargetCount);
                if (progress != t.ProgressCount) { t.ProgressCount = progress; changed = true; }
                if (t.Status == StudyTaskStatus.Pending && progress >= t.TargetCount && t.TargetCount > 0)
                {
                    t.Status = StudyTaskStatus.Completed;
                    t.CompletedAt = DateTime.UtcNow;
                    changed = true;
                }
            }
            if (changed) await _db.SaveChangesAsync(ct);
        }

        private static PlanSummaryDto Summarise(DateOnly day, IEnumerable<StudyPlanTask> tasks)
        {
            var list = tasks.ToList();
            return new PlanSummaryDto
            {
                Date = day.ToString("yyyy-MM-dd"),
                Total = list.Count,
                Completed = list.Count(t => t.Status == StudyTaskStatus.Completed),
                Dismissed = list.Count(t => t.Status == StudyTaskStatus.Dismissed),
                Pending = list.Count(t => t.Status == StudyTaskStatus.Pending)
            };
        }

        private static StudyTaskDto ToDto(StudyPlanTask t, PracticeTargetResolver? resolver) => new()
        {
            Id = t.Id,
            Type = t.TaskType.ToString(),
            Title = t.Title,
            Reason = t.Reason,
            TargetCount = t.TargetCount,
            ProgressCount = t.ProgressCount,
            Status = t.Status.ToString(),
            Subject = t.SubjectName,
            Topic = t.TopicName,
            Priority = t.Priority,
            PracticeTarget = resolver != null && t.TaskType == StudyTaskType.PracticeTopic
                ? resolver.Resolve(t.SubjectName, t.TopicName, null)
                : null
        };

        // ------------------------------------------------------------------ API

        public async Task<StudyPlanDto> GetTodayAsync(Guid userId, IReadOnlyCollection<Guid>? examIds, CancellationToken ct)
        {
            var day = AnalyticsCalculator.IstDay(UtcNow);

            var tasks = await _db.StudyPlanTasks.Where(t => t.UserId == userId && t.PlanDate == day).ToListAsync(ct);
            tasks = await EnsureGeneratedAsync(userId, day, examIds, tasks, ct);
            await RefreshProgressAsync(userId, day, tasks, ct);

            // The resolver is only used for practice tasks' PracticeTarget, so skip its queries when there are none.
            var resolver = tasks.Any(t => t.TaskType == StudyTaskType.PracticeTopic) ? await ResolverAsync(ct) : null;
            var plan = new StudyPlanDto
            {
                Date = day.ToString("yyyy-MM-dd"),
                Summary = Summarise(day, tasks),
                Tasks = tasks.OrderBy(t => t.Priority).ThenBy(t => t.Title, StringComparer.Ordinal).Select(t => ToDto(t, resolver)).ToList()
            };
            if (tasks.Count == 0)
                plan.Message = "Complete a practice test, mock test or previous year paper to unlock a plan built from your own results.";
            return plan;
        }

        public async Task<PlanSummaryDto> GetSummaryAsync(Guid userId, DateOnly day, CancellationToken ct)
        {
            var tasks = await _db.StudyPlanTasks.AsNoTracking().Where(t => t.UserId == userId && t.PlanDate == day).ToListAsync(ct);
            return Summarise(day, tasks);
        }

        public async Task<StudyPlanHistoryDto> GetHistoryAsync(Guid userId, int days, CancellationToken ct)
        {
            days = Math.Clamp(days, 1, 60);
            var today = AnalyticsCalculator.IstDay(UtcNow);
            var from = today.AddDays(-(days - 1));
            var tasks = await _db.StudyPlanTasks.AsNoTracking()
                .Where(t => t.UserId == userId && t.PlanDate >= from && t.PlanDate <= today)
                .ToListAsync(ct);
            var resolver = await PracticeTargetResolver.LoadAsync(_db, ct);

            return new StudyPlanHistoryDto
            {
                Days = tasks.GroupBy(t => t.PlanDate)
                    .OrderByDescending(g => g.Key)
                    .Select(g => new StudyPlanHistoryDayDto
                    {
                        Date = g.Key.ToString("yyyy-MM-dd"),
                        Summary = Summarise(g.Key, g),
                        Tasks = g.OrderBy(t => t.Priority).Select(t => ToDto(t, resolver)).ToList()
                    }).ToList()
            };
        }

        public async Task<StudyTaskUpdateOutcome> SetDismissedAsync(Guid userId, Guid taskId, bool dismissed, CancellationToken ct)
        {
            var task = await _db.StudyPlanTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct);
            if (task == null) return new StudyTaskUpdateOutcome(null, 404, "Task not found.");

            var today = AnalyticsCalculator.IstDay(UtcNow);
            if (task.PlanDate != today) return new StudyTaskUpdateOutcome(null, 409, "Only today's tasks can be changed.");

            if (dismissed)
            {
                if (task.Status == StudyTaskStatus.Completed)
                    return new StudyTaskUpdateOutcome(null, 409, "A completed task can't be dismissed.");
                task.Status = StudyTaskStatus.Dismissed;
                task.DismissedAt = DateTime.UtcNow;
            }
            else if (task.Status == StudyTaskStatus.Dismissed)
            {
                task.Status = StudyTaskStatus.Pending;
                task.DismissedAt = null;
            }
            await _db.SaveChangesAsync(ct);

            // Restoring may immediately be complete (activity already happened today).
            if (!dismissed)
            {
                var one = new List<StudyPlanTask> { task };
                await RefreshProgressAsync(userId, today, one, ct);
            }
            var resolver = await PracticeTargetResolver.LoadAsync(_db, ct);
            return new StudyTaskUpdateOutcome(ToDto(task, resolver), 200, null);
        }

        public async Task<StudyPlanShareSummaryDto> GetWeeklyShareSummaryAsync(Guid userId, CancellationToken ct)
        {
            var today = AnalyticsCalculator.IstDay(UtcNow);
            var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            var tasks = await _db.StudyPlanTasks.AsNoTracking()
                .Where(t => t.UserId == userId && t.PlanDate >= monday && t.PlanDate <= today && t.Status != StudyTaskStatus.Dismissed)
                .Select(t => new { t.PlanDate, t.Status })
                .ToListAsync(ct);

            var weekStartUtc = AnalyticsCalculator.DayBoundsUtc(monday).StartUtc;
            var attemptTimes = await AttemptQueries.CompletedAttempts(_db, userId, null, null, weekStartUtc, null)
                .Select(r => r.AttemptedAt).ToListAsync(ct);
            var revisionTimes = await _db.RevisionAttempts.AsNoTracking()
                .Where(r => r.UserId == userId && r.AnsweredAt >= weekStartUtc).Select(r => r.AnsweredAt).ToListAsync(ct);

            var planned = tasks.Count;
            var done = tasks.Count(t => t.Status == StudyTaskStatus.Completed);
            return new StudyPlanShareSummaryDto
            {
                WeekStart = monday.ToString("yyyy-MM-dd"),
                PlannedTasks = planned,
                CompletedTasks = done,
                CompletionPercent = AnalyticsCalculator.Percent(done, planned),
                ActiveDays = attemptTimes.Concat(revisionTimes).Select(AnalyticsCalculator.IstDay).Distinct().Count()
            };
        }
    }
}
