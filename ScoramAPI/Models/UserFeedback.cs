using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    // USER FEEDBACK -- a student's suggestion / improvement / bug report sent from the floating
    // Feedback button on Home (web + Flutter). Deliberately a NEW table: the existing report tables
    // (QuestionReport, ChatReport, CommentReport) are each tied to one specific piece of content and
    // moderated through their own workflow; general product feedback has no such target.
    public class UserFeedback
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // Human-readable running number shown to admins as "FDB-00042". Database-generated identity
        // (the key stays the Guid, like every other entity here) -- see ScoramDbContext.
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int FeedbackNumber { get; set; }

        // Taken from the authenticated principal only -- never from the request body.
        public Guid UserId { get; set; }
        public User? User { get; set; }

        public FeedbackType FeedbackType { get; set; }

        [Required, MaxLength(2000)]
        public string Message { get; set; } = string.Empty;

        // Optional 1-5 star rating; null when the student didn't give one.
        public int? Rating { get; set; }

        // Where in the app it was sent from ("Home", ...). Free text, best-effort, optional.
        [MaxLength(100)]
        public string? Source { get; set; }

        public FeedbackPlatform Platform { get; set; } = FeedbackPlatform.Web;

        public FeedbackStatus Status { get; set; } = FeedbackStatus.New;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
