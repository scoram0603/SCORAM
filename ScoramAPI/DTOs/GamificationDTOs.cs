namespace ScoramAPI.DTOs
{
    // ==================================================================================
    // GAMIFICATION -- Streak/XP/Levels/Badges/Leaderboard. See Services/GamificationService.cs
    // for where every number here actually gets computed; this file is response shapes only.
    // ==================================================================================

    // GET /api/gamification/me -- powers the home-page streak/XP card and a future profile tab.
    public class GamificationSummaryDto
    {
        public int CurrentStreak { get; set; }
        public int LongestStreak { get; set; }
        public int FreezesUsedThisWeek { get; set; }
        public int FreezesAvailableThisWeek { get; set; }
        public DateOnly LastActiveDate { get; set; }

        public int TotalXP { get; set; }
        public string CurrentLevel { get; set; } = string.Empty;
        public string? NextLevel { get; set; }
        public int? XpToNextLevel { get; set; }

        public int BadgeCount { get; set; }
        public int BonusMockAttempts { get; set; }
    }

    // GET /api/gamification/badges -- the full master list, each flagged with whether the current
    // student has earned it yet (and when), so the frontend can render locked/unlocked in one call.
    public class BadgeDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? IconUrl { get; set; }
        public string CriteriaDescription { get; set; } = string.Empty;
        public bool Earned { get; set; }
        public DateTime? EarnedAt { get; set; }
    }

    // GET /api/gamification/leaderboard?scope=global|exam|friends&period=alltime|weekly|monthly&examName=
    public class LeaderboardEntryDto
    {
        public int Rank { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? PhotoUrl { get; set; }
        public int Xp { get; set; }
        // True when this row is the requesting student themselves -- lets the frontend highlight
        // "you are here" without a second lookup, including when they're outside the top N (see
        // GamificationController.Leaderboard, which always appends the caller's own row/rank).
        public bool IsCurrentUser { get; set; }
    }

    public class LeaderboardResponseDto
    {
        public string Scope { get; set; } = string.Empty;
        public string Period { get; set; } = string.Empty;
        public string? ExamName { get; set; }
        public List<LeaderboardEntryDto> Entries { get; set; } = new();
    }

    // GET /api/referrals/me
    public class ReferralSummaryDto
    {
        public string ReferralCode { get; set; } = string.Empty;
        public string ShareText { get; set; } = string.Empty;
        public int TotalJoins { get; set; }
        public int TotalXpEarned { get; set; }
        public int BonusMockAttempts { get; set; }
        public List<ReferralItemDto> Referrals { get; set; } = new();
    }

    public class ReferralItemDto
    {
        public string ReferredFullName { get; set; } = string.Empty;
        public DateTime? JoinedAt { get; set; }
    }

    // GET /api/gamification/progress-analytics
    public class ProgressAnalyticsDto
    {
        public List<SubjectPerformanceDto> BySubject { get; set; } = new();
        public List<ActivityPerformanceDto> ByActivity { get; set; } = new();
        public List<ScoreTrendPointDto> RecentScoreTrend { get; set; } = new();
    }

    public class SubjectPerformanceDto
    {
        public string Subject { get; set; } = string.Empty;
        public int Attempted { get; set; }
        public int Correct { get; set; }
        public decimal AccuracyPercent { get; set; }
    }

    public class ActivityPerformanceDto
    {
        public string TestKind { get; set; } = string.Empty; // "Practice" | "Mock" | "PreviousYearPaper" | "Quiz"
        public int AttemptCount { get; set; }
        public decimal AvgScorePercent { get; set; }
    }

    public class ScoreTrendPointDto
    {
        public DateTime Date { get; set; }
        public decimal ScorePercent { get; set; }
        public string TestKind { get; set; } = string.Empty;
    }
}
