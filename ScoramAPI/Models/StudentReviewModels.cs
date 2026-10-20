using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.Models
{
    // STUDENT REVIEWS -- a student's public "Rate SCORAM" review (1-5 stars + text), submitted from the
    // website and shown on the landing page ONLY after an admin approves it.
    //
    // Deliberately NOT UserFeedback: that table is private, triage-oriented product feedback (suggestions,
    // bug reports) with no public-visibility concept, no one-per-student rule and no moderation states.
    // Mixing public testimonials into it would risk leaking private feedback through a public query.
    //
    // Visibility rule (enforced in ReviewsController.PublicList, never only in the client):
    //   Status == Approved  AND  Rating >= ReviewRules.MinPublicRating  AND  author account is active.
    // Rows rated below that are kept (moderation / internal reporting) and simply never listed publicly.
    public enum ReviewStatus
    {
        Pending,   // default on submit and after every student edit -- never public
        Approved,  // public if rating >= MinPublicRating
        Rejected,  // moderator declined; the student may edit and resubmit (goes back to Pending)
        Hidden     // was visible (or flagged) and pulled by a moderator; student cannot re-publish by editing
    }

    public class StudentReview
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // Taken from the authenticated principal only -- never from the request body. Unique: one review
        // per student (they edit or withdraw it instead of posting more).
        public Guid UserId { get; set; }
        public User? User { get; set; }

        // 1..5, validated server-side (ReviewRules.Validate).
        public int Rating { get; set; }

        [Required, MaxLength(1000)]
        public string Text { get; set; } = string.Empty;

        // Consent for how the author is labelled publicly. false (default) => "SCORAM Student";
        // true => first name + last initial ("Priya S."). Email / phone / username are never shown.
        public bool ShowNamePublicly { get; set; } = false;

        public ReviewStatus Status { get; set; } = ReviewStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Moderation trail (internal -- never returned by any public or student endpoint).
        public DateTime? ModeratedAt { get; set; }
        public Guid? ModeratedByAdminId { get; set; }
        [MaxLength(500)]
        public string? ModerationNote { get; set; }
    }

    // LANDING CONTENT -- admin-managed landing-page media references and the Smart Progress section copy.
    // One small key/value-style table (like AppSetting) so adding a slot later is a code change in
    // LandingContentCatalog, never a schema migration.
    //   * Key "media:{slot}"   -> MediaUrl holds "/uploads/landing/{guid}.ext" (an existing blob served by
    //                            UploadedFilesController). No image bytes ever live in this table.
    //   * Key "smart-progress" -> TextJson holds the validated SmartProgressContent JSON.
    // A missing row means "use the website's bundled default", so nothing here can break a fresh install.
    public class LandingContentItem
    {
        [Key, MaxLength(80)]
        public string Key { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? MediaUrl { get; set; }

        public string? TextJson { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // No FK to Admins (same reasoning as AppSetting): the row must survive an admin account change;
        // the audit log records who did it.
        public Guid? UpdatedByAdminId { get; set; }
    }
}
