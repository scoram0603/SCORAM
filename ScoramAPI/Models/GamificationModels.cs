using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    public class UserStreak
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public int CurrentStreak { get; set; } = 0;

        public int LongestStreak { get; set; } = 0;

        public DateOnly LastActiveDate { get; set; }

        public int FreezesUsedThisWeek { get; set; } = 0;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class UserXP
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public int TotalXP { get; set; } = 0;

        public UserLevel CurrentLevel { get; set; } = UserLevel.Beginner;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Badge
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;   // "10-Second Trick Master", "Top Contributor", etc.

        public string? Description { get; set; }

        public string? IconUrl { get; set; }

        public string? CriteriaDescription { get; set; }

        public ICollection<UserBadge> UserBadges { get; set; } = new List<UserBadge>();
    }

    public class UserBadge
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public Guid BadgeId { get; set; }
        public Badge? Badge { get; set; }

        public DateTime EarnedAt { get; set; } = DateTime.UtcNow;
    }

    // GAMIFICATION -- one row per successful referral (a student who registered using someone else's
    // code). ReferralCode here is a copy of ReferrerUser.ReferralCode (see User.cs) at the time of
    // signup -- the actual unique, permanent, shareable code lives on User, not here, since the same
    // code is reused across every person a student refers. That's also why this table's ReferralCode
    // column is *not* unique (see ScoramDbContext) -- many rows legitimately share one referrer's code.
    public class Referral
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ReferrerUserId { get; set; }
        public User? ReferrerUser { get; set; }

        [Required, MaxLength(20)]
        public string ReferralCode { get; set; } = string.Empty;

        public Guid? ReferredUserId { get; set; }
        public User? ReferredUser { get; set; }

        public ReferralStatus Status { get; set; } = ReferralStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? JoinedAt { get; set; }
    }

    // GAMIFICATION -- an append-only audit log of every XP grant. Two things this unlocks beyond a
    // running UserXP.TotalXP counter: (1) weekly/monthly leaderboards, computed by summing Amount
    // within a date range, and (2) exam-wise leaderboards via ExamName. Reason is a short
    // machine-readable tag (e.g. "MockTestCompleted", "SolutionApproved") rather than a free-text
    // description -- see GamificationService for the fixed set of values actually written.
    public class XpTransaction
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public int Amount { get; set; }

        [Required, MaxLength(50)]
        public string Reason { get; set; } = string.Empty;

        // Which exam this XP counts toward on the exam-wise leaderboard (MockTest.ExamName /
        // Exam.Name, kept as a plain string for the same reason MockTest itself does -- see
        // GamificationService). Null for actions with no single obvious exam (e.g. a solution
        // upvote), which simply don't contribute to any exam-wise leaderboard.
        [MaxLength(100)]
        public string? ExamName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // GAMIFICATION -- "solving a question" in the Question Bank (PYQ Bank) isn't tracked anywhere
    // else in the app; this is the new minimal record of it. One row per (user, question) --
    // enforced with a unique index in ScoramDbContext -- so re-visiting/re-marking a question already
    // solved doesn't farm XP or streak credit a second time.
    public class UserQuestionSolve
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public Guid QuestionBankQuestionId { get; set; }
        public QuestionBankQuestion? QuestionBankQuestion { get; set; }

        public DateTime SolvedAt { get; set; } = DateTime.UtcNow;
    }
}
