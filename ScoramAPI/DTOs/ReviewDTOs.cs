namespace ScoramAPI.DTOs
{
    // ---- student (own review) ----
    public class UpsertReviewDto
    {
        public int? Rating { get; set; }
        public string? Text { get; set; }
        public bool ShowNamePublicly { get; set; }
    }

    public class MyReviewDto
    {
        public Guid Id { get; set; }
        public int Rating { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool ShowNamePublicly { get; set; }
        // Pending | Approved | Rejected | Hidden -- the student's own review's state. Internal moderation
        // notes are never included.
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class MyReviewResponseDto
    {
        public MyReviewDto? Review { get; set; }
    }

    // ---- public listing (anonymous) -- safe fields only ----
    public class PublicReviewDto
    {
        public Guid Id { get; set; }
        public int Rating { get; set; }
        public string Text { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    // ---- admin moderation ----
    public class AdminReviewDto
    {
        public Guid Id { get; set; }
        public int Rating { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool ShowNamePublicly { get; set; }
        // Exactly what the public site would print for this review.
        public string PublicName { get; set; } = string.Empty;
        // Admin-only context so a moderator can recognise a repeat offender. No email / phone.
        public string AuthorFullName { get; set; } = string.Empty;
        public string? AuthorUsername { get; set; }
        public bool AuthorActive { get; set; }
        // True when the public landing page would currently show it (Approved + rating >= 3 + active author).
        public bool PublicOnLanding { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ModeratedAt { get; set; }
        public string? ModeratedBy { get; set; }
        public string? ModerationNote { get; set; }
    }

    public class AdminReviewSummaryDto
    {
        public int Total { get; set; }
        public int Pending { get; set; }
        public int Approved { get; set; }
        public int Rejected { get; set; }
        public int Hidden { get; set; }
        // Mean of ALL stored ratings (every status) -- an internal figure, never published.
        public double? AverageRatingAll { get; set; }
        public int PublicOnLanding { get; set; }
    }

    public class AdminReviewListDto
    {
        public List<AdminReviewDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public AdminReviewSummaryDto Summary { get; set; } = new();
    }

    public class UpdateReviewStatusDto
    {
        // Pending | Approved | Rejected | Hidden
        public string? Status { get; set; }
        public string? Note { get; set; }
    }
}
