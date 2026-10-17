using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.Models
{
    // SUPPORT WORKFLOW -- one entry in a feedback/ticket's timeline. UserFeedback itself is unchanged
    // (existing records are untouched); notes, assignment and priority extend it additively.
    //
    //   Kind = "Note"      -- internal-only comment, never shown to the student.
    //   Kind = "Response"  -- a reply that was also delivered to the student as an in-app/push notification
    //                         (see SupportTicketsController.Respond).
    //   Kind = "Event"     -- system-written history line (assigned to X, priority changed, ...).
    public class FeedbackNote
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid FeedbackId { get; set; }
        public UserFeedback? Feedback { get; set; }

        // Null only for nothing today; kept nullable so a future system-generated event needs no actor.
        public Guid? AdminId { get; set; }
        public Admin? Admin { get; set; }

        [Required, MaxLength(10)]
        public string Kind { get; set; } = "Note";

        [Required, MaxLength(2000)]
        public string Body { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
