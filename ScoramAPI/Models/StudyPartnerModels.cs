using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    // STUDY PARTNER -- a mutual, exam-preparation connection between two students. Built ON the
    // existing User / Exam / StudentTestResult / UserStreak / DirectConversation tables; none of those
    // are duplicated or altered.

    /// <summary>A pending/answered request from one student to another. Once Accepted, the pair is
    /// represented by exactly ONE <see cref="StudyPartnership"/> row (never two mirrored rows).</summary>
    public class StudyPartnerRequest
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid SenderUserId { get; set; }
        public User? Sender { get; set; }

        public Guid ReceiverUserId { get; set; }
        public User? Receiver { get; set; }

        public StudyPartnerRequestStatus Status { get; set; } = StudyPartnerRequestStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? RespondedAt { get; set; }
    }

    /// <summary>The mutual relationship. ONE row per pair: UserAId/UserBId are stored in canonical
    /// order (see StudyPartnerRules.Canonical) so "A-B" and "B-A" are the same row and a unique index
    /// makes duplicates impossible. Removing a Study Partner deletes the row.</summary>
    public class StudyPartnership
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserAId { get; set; }
        public User? UserA { get; set; }

        public Guid UserBId { get; set; }
        public User? UserB { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>SCORAM had no user-blocking before this; it is deliberately generic (not
    /// Study-Partner-specific) so messaging/search can honour it too -- the Direct Messages controller
    /// and user search now check it.</summary>
    public class UserBlock
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid BlockerUserId { get; set; }
        public User? Blocker { get; set; }

        public Guid BlockedUserId { get; set; }
        public User? Blocked { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>One optional row per student (no row = privacy-conscious defaults, see
    /// StudyPartnerRules.DefaultPrivacy). Enforced on the BACKEND for every endpoint that exposes
    /// another student's data.</summary>
    public class UserPrivacySettings
    {
        [Key]
        public Guid UserId { get; set; }
        public User? User { get; set; }

        /// <summary>Selected exams on the profile / in search. Name, username and photo are already
        /// visible app-wide (Direct Messages search) so they are only hidden by OnlyMe.</summary>
        public VisibilityLevel ProfileVisibility { get; set; } = VisibilityLevel.Everyone;
        public VisibilityLevel ProgressVisibility { get; set; } = VisibilityLevel.StudyPartnersOnly;
        public VisibilityLevel ActivityVisibility { get; set; } = VisibilityLevel.StudyPartnersOnly;

        /// <summary>False = hidden from OTHER students' Study Partner leaderboards.</summary>
        public bool ShowOnLeaderboard { get; set; } = true;
        /// <summary>False = nobody can send this student a new Study Partner request.</summary>
        public bool AllowStudyPartnerRequests { get; set; } = true;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class StudyPartnerChallenge
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CreatorUserId { get; set; }
        public User? Creator { get; set; }

        public Guid PartnerUserId { get; set; }
        public User? Partner { get; set; }

        public StudyChallengeType Type { get; set; }

        /// <summary>Optional: only attempts for this exam count. Must be in BOTH students' My Exams.</summary>
        public Guid? ExamId { get; set; }
        public Exam? Exam { get; set; }

        /// <summary>Practice/Accuracy/Speed target. Null for Streak.</summary>
        public int? QuestionCount { get; set; }
        /// <summary>Streak target in days. Null otherwise.</summary>
        public int? TargetDays { get; set; }

        /// <summary>Stored for display only -- attempts are the existing Practice/Mock engine's, so a
        /// per-challenge timer isn't enforced (see StudyPartnersController).</summary>
        public int? TimeLimitMinutes { get; set; }

        public StudyChallengeStatus Status { get; set; } = StudyChallengeStatus.Pending;

        /// <summary>How long the challenge runs once the partner accepts (1-30).</summary>
        public int DurationDays { get; set; } = 7;

        /// <summary>Both set when the partner accepts (Start = now, End = Start + DurationDays). Only
        /// attempts submitted within [StartDate, EndDate] count.</summary>
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        /// <summary>Final numbers, written once when the challenge completes (see
        /// StudyPartnerRules.Evaluate for what each type's number means).</summary>
        public decimal? CreatorResult { get; set; }
        public decimal? PartnerResult { get; set; }
        public Guid? WinnerUserId { get; set; }

        [MaxLength(200)]
        public string? ResultSummary { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? RespondedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
