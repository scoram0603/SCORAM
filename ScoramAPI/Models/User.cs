using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.Models
{
    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(30)]
        public string Username { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required, MaxLength(150)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        public string? PhotoUrl { get; set; }

        public bool IsActive { get; set; } = true;

        // Notification preferences -- default true for both (see ScoramDbContext for the matching
        // HasDefaultValue(true), which also backfills existing rows to "on" when the migration runs).
        // Deliberately just two global switches, not per-room/per-conversation muting -- that's a
        // natural follow-up if it's ever wanted, but isn't what was asked for here.
        public bool NotifyOnGroupMessages { get; set; } = true;
        public bool NotifyOnDirectMessages { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // GAMIFICATION -- this student's own permanent, shareable referral code (SRS: "unique
        // referral code and shareable link per student"). Generated lazily on first request rather
        // than at registration -- see GamificationService.GetOrCreateReferralCodeAsync -- so it costs
        // nothing for the students who never open the Referral tab. Null until then.
        [MaxLength(20)]
        public string? ReferralCode { get; set; }

        // GAMIFICATION -- extra Mock Test attempts earned from successful referrals, on top of
        // whatever MockTest.MaxAttempts already allows (see MockTestsController.Start). A simple
        // global pool rather than a per-test ledger: +1 here raises the attempt ceiling by 1 on
        // every attempt-capped mock test, not just one specific test.
        public int BonusMockAttempts { get; set; } = 0;

        // Updated on every successful login (see AuthController.Login). Not updated on every request
        // -- that would mean a DB write per authenticated request, which matters at this app's traffic
        // scale. Login-only is a reasonable proxy for "active today" since tokens expire every
        // ExpiryMinutes (currently 24h), so a genuinely active student re-logs in roughly daily anyway.
        public DateTime? LastActiveAt { get; set; }

        // Navigation properties
        public ICollection<QuestionSolution> QuestionSolutions { get; set; } = new List<QuestionSolution>();
        public ICollection<QuestionReport> QuestionReports { get; set; } = new List<QuestionReport>();
        public ICollection<QuestionComment> QuestionComments { get; set; } = new List<QuestionComment>();
        public ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();
        public ICollection<StudentTestResult> StudentTestResults { get; set; } = new List<StudentTestResult>();
        public ICollection<StudentSyllabusProgress> SyllabusProgress { get; set; } = new List<StudentSyllabusProgress>();
        public ICollection<TypingTestResult> TypingTestResults { get; set; } = new List<TypingTestResult>();
        public ICollection<UserBadge> UserBadges { get; set; } = new List<UserBadge>();

        public UserStreak? UserStreak { get; set; }
        public UserXP? UserXP { get; set; }
    }
}
