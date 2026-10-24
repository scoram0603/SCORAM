using Microsoft.EntityFrameworkCore;
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

        public StudyPlanService(ScoramDbContext db, IProgressAnalyticsService analytics, IRevisionService revision, IOptions<AnalyticsOptions> options)
        {
            _db = db;
            _analytics = analytics;
            _revision = revision;
            _o = options.Value;
        }

        private static string PracticeKey(string subject, string topic) => $"practice:{subject}|{topic}";

        // ------------------------------------------------------------------ generation

        private async Task EnsureGeneratedAsync(Guid userId, DateOnly day, IReadOnlyCollection<Guid>? examIds, CancellationToken ct)
        {
            var existing = await _db.StudyPlanTasks.AsNoTracking()
                .Where(t => t.UserId == userId && t.PlanDate == day)
                .Select(t => new { t.TaskKey, t.TaskType })
                .ToListAsync(ct);
            var haveKeys = existing.Select(e => e.TaskKey).ToHashSet(StringComparer.Ordinal);
            var practiceCount = existing.Count(e => e.TaskType == StudyTaskType.PracticeTopic);

            var candidates = new List<StudyPlanTask>();
            var now = DateTime.UtcNow;

            // 1. Revise due mistakes.
            var states = await _revision.GetMistakeStatesAsync(userId, new MistakeQuery(), ct);
            var open = states.Where(s => s.Wrong > 0 && s.IsOpen).ToList();
            var due = open.Count(s => s.IsDue);
            if (due > 0 && !haveKeys.Contains("revise"))
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

            // 2. Practise the weakest topics (never Unclassified, never topics without enough data).
            var filter = new ProgressFilter { ExamIds = examIds is { Count: > 0 } ? examIds : null };
            var insights = await _analytics.GetTopicInsightsAsync(userId, filter, ct);
            var weak = insights
                .Where(t => !t.IsUnclassified && (t.Status == "NeedsAttention" || t.Status == "PracticeRecommended" || t.Status == "NeedsRetesting"))
                .ToList();
            var slots = Math.Max(0, _o.StudyPlanMaxPracticeTasks - practiceCount);
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

            // 3. One timed session, once the student has any completed history.
            var completed = await AttemptQueries.CompletedAttempts(_db, userId, null, null, null, null).CountAsync(ct);
            if (completed > 0 && !haveKeys.Contains("timed"))
            {
                var last = await AttemptQueries.CompletedAttempts(_db, userId, null, null, null, null)
                    .OrderByDescending(r => r.AttemptedAt).Select(r => (DateTime?)r.AttemptedAt).FirstOrDefaultAsync(ct);
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

            if (candidates.Count == 0) return;
            _db.StudyPlanTasks.AddRange(candidates);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A concurrent request generated the same keys first (unique index on user+day+key): that's fine.
                _db.ChangeTracker.Clear();
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
            var day = AnalyticsCalculator.IstDay(DateTime.UtcNow);
            // Generation is deliberately NOT tied to the request's cancellation token. The first read of the
            // day does the heavy work (mistake states, topic insights, history counts); if the client gave
            // up waiting (mobile timeout on a cold/busy server) and that work were cancelled with it, the
            // plan would never get saved and every retry would start from zero. Letting it finish means the
            // client's retry is answered from the saved tasks almost instantly. It is idempotent (unique
            // key per user/day/task), so a duplicate or abandoned run is harmless.
            await EnsureGeneratedAsync(userId, day, examIds, CancellationToken.None);

            var tasks = await _db.StudyPlanTasks.Where(t => t.UserId == userId && t.PlanDate == day).ToListAsync(ct);
            await RefreshProgressAsync(userId, day, tasks, ct);

            var resolver = await PracticeTargetResolver.LoadAsync(_db, ct);
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
            var today = AnalyticsCalculator.IstDay(DateTime.UtcNow);
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

            var today = AnalyticsCalculator.IstDay(DateTime.UtcNow);
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
            var today = AnalyticsCalculator.IstDay(DateTime.UtcNow);
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
