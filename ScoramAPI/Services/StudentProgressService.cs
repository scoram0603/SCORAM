using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Enums;

namespace ScoramAPI.Services
{
    /// <summary>Per-student study numbers. Definitions deliberately MATCH the existing Progress page
    /// (GamificationController.ProgressAnalytics): an attempt counts once it has left InProgress; a
    /// question counts as "attempted" only if an option was selected (skipped != attempted); accuracy =
    /// correct / attempted. Streak and XP come straight from UserStreak / UserXP, the same rows the
    /// Progress page reads -- Study Partner never keeps its own copy of any of these.</summary>
    public sealed class ProgressSummary
    {
        public int QuestionsAttempted { get; set; }
        public int QuestionsCorrect { get; set; }
        public decimal AccuracyPercent => QuestionsAttempted == 0 ? 0 : Math.Round((decimal)QuestionsCorrect / QuestionsAttempted * 100, 1);
        public int PracticeTests { get; set; }
        public int MockTests { get; set; }
        public int PypAttempts { get; set; }
        public int Quizzes { get; set; }
        public int TestsCompleted => PracticeTests + MockTests + PypAttempts + Quizzes;
        public int CurrentStreak { get; set; }
        public int LongestStreak { get; set; }
        public int TotalXp { get; set; }
        public int Badges { get; set; }
        /// <summary>Questions attempted in the last 7 days -- the "study activity" number.</summary>
        public int QuestionsLast7Days { get; set; }
    }

    /// <summary>One completed attempt, trimmed to what Study Partner needs.</summary>
    public sealed record AttemptRow(Guid UserId, TestKind Kind, Guid? ExamId, DateTime AttemptedAt,
        int TimeTakenSeconds, int Answered, int Correct);

    public interface IStudentProgressService
    {
        /// <summary>Completed attempts for many students in ONE query (no per-student round trips),
        /// optionally narrowed to some exams and/or a time window. Quizzes carry no exam, so any exam
        /// filter excludes them.</summary>
        Task<List<AttemptRow>> GetAttemptsAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<Guid>? examIds = null,
            DateTime? from = null, DateTime? to = null, CancellationToken ct = default);

        Task<Dictionary<Guid, ProgressSummary>> GetSummariesAsync(IReadOnlyCollection<Guid> userIds,
            IReadOnlyCollection<Guid>? examIds = null, DateTime? from = null, CancellationToken ct = default);

        /// <summary>UTC instant of the start of the current IST day (the app's streak day boundary).</summary>
        DateTime StartOfTodayUtc();
    }

    public class StudentProgressService : IStudentProgressService
    {
        private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);
        private readonly ScoramDbContext _db;

        public StudentProgressService(ScoramDbContext db) => _db = db;

        public DateTime StartOfTodayUtc() => (DateTime.UtcNow + Ist).Date - Ist;

        public async Task<List<AttemptRow>> GetAttemptsAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<Guid>? examIds = null,
            DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
        {
            var query = _db.StudentTestResults
                .AsNoTracking()
                .Where(r => userIds.Contains(r.UserId) && r.Status != TestAttemptStatus.InProgress);

            if (from.HasValue) query = query.Where(r => r.AttemptedAt >= from.Value);
            if (to.HasValue) query = query.Where(r => r.AttemptedAt <= to.Value);

            // The exam an attempt belongs to: its Mock Test's, Practice template's or Paper's exam, or the
            // exam chosen for an ad-hoc Practice attempt. Matches how each test kind records its exam.
            if (examIds is { Count: > 0 })
            {
                query = query.Where(r => examIds.Contains(
                    r.MockTest!.ExamId ?? r.PracticeTestTemplate!.ExamId ?? (Guid?)r.Paper!.ExamId ?? r.PracticeExamId ?? Guid.Empty));
            }

            return await query
                .Select(r => new AttemptRow(
                    r.UserId,
                    r.TestKind,
                    r.MockTest!.ExamId ?? r.PracticeTestTemplate!.ExamId ?? (Guid?)r.Paper!.ExamId ?? r.PracticeExamId,
                    r.AttemptedAt,
                    r.TimeTakenSeconds,
                    r.Answers.Count(a => a.SelectedOption != null),
                    r.Answers.Count(a => a.SelectedOption != null && a.IsCorrect)))
                .ToListAsync(ct);
        }

        public async Task<Dictionary<Guid, ProgressSummary>> GetSummariesAsync(IReadOnlyCollection<Guid> userIds,
            IReadOnlyCollection<Guid>? examIds = null, DateTime? from = null, CancellationToken ct = default)
        {
            var result = userIds.Distinct().ToDictionary(id => id, _ => new ProgressSummary());
            if (result.Count == 0) return result;

            var attempts = await GetAttemptsAsync(result.Keys.ToList(), examIds, from, null, ct);
            var weekAgo = DateTime.UtcNow.AddDays(-7);

            foreach (var a in attempts)
            {
                var s = result[a.UserId];
                s.QuestionsAttempted += a.Answered;
                s.QuestionsCorrect += a.Correct;
                if (a.AttemptedAt >= weekAgo) s.QuestionsLast7Days += a.Answered;
                switch (a.Kind)
                {
                    case TestKind.Practice: s.PracticeTests++; break;
                    case TestKind.Mock: s.MockTests++; break;
                    case TestKind.PreviousYearPaper: s.PypAttempts++; break;
                    case TestKind.Quiz: s.Quizzes++; break;
                }
            }

            // Streak / XP / badges are whole-account numbers (not per exam or period) -- the same
            // meaning they have on the Progress page.
            var ids = result.Keys.ToList();
            foreach (var st in await _db.UserStreaks.AsNoTracking().Where(x => ids.Contains(x.UserId))
                         .Select(x => new { x.UserId, x.CurrentStreak, x.LongestStreak }).ToListAsync(ct))
            {
                result[st.UserId].CurrentStreak = st.CurrentStreak;
                result[st.UserId].LongestStreak = st.LongestStreak;
            }
            foreach (var xp in await _db.UserXPs.AsNoTracking().Where(x => ids.Contains(x.UserId))
                         .Select(x => new { x.UserId, x.TotalXP }).ToListAsync(ct))
                result[xp.UserId].TotalXp = xp.TotalXP;
            foreach (var b in await _db.UserBadges.AsNoTracking().Where(x => ids.Contains(x.UserId))
                         .GroupBy(x => x.UserId).Select(g => new { UserId = g.Key, Count = g.Count() }).ToListAsync(ct))
                result[b.UserId].Badges = b.Count;

            return result;
        }
    }
}
