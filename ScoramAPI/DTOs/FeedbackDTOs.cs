using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.DTOs
{
    // USER FEEDBACK -- see Controllers/FeedbackController.cs.

    // POST /api/feedback. FeedbackType + Message are the only required fields.
    public class FeedbackCreateDto
    {
        // Suggestion | Improvement | BugReport | ContentIssue | UiUx | Other
        [Required]
        public string FeedbackType { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;

        // Optional, 1-5.
        public int? Rating { get; set; }

        // Optional: which screen it was sent from, e.g. "Home".
        public string? Source { get; set; }

        // "Web" | "Flutter"
        public string? Platform { get; set; }
    }

    public class FeedbackSubmittedDto
    {
        public Guid Id { get; set; }
        public string FeedbackCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    // Admin list row / detail. Only what's needed to handle feedback: the student's display name and
    // username -- deliberately NO email, phone, password hash, tokens or any auth data.
    public class AdminFeedbackDto
    {
        public Guid Id { get; set; }
        public string FeedbackCode { get; set; } = string.Empty;
        public string UserFullName { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string FeedbackType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string MessagePreview { get; set; } = string.Empty;
        public int? Rating { get; set; }
        public string Platform { get; set; } = string.Empty;
        public string? Source { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class UpdateFeedbackStatusDto
    {
        // New | InReview | Resolved | Rejected
        [Required]
        public string Status { get; set; } = string.Empty;
    }
}
