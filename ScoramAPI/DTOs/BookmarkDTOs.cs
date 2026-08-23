namespace ScoramAPI.DTOs
{
    // Returned by every POST .../bookmark toggle endpoint.
    public class BookmarkToggleResponseDto
    {
        public bool IsBookmarked { get; set; }
    }

    // What kind of content a BookmarkListItemDto points at -- lets the frontend pick the right
    // card/route without needing to guess from which fields happen to be non-null.
    public enum BookmarkType
    {
        Question,             // legacy Paper-based question
        QuestionBankQuestion,
        Discussion,           // a top-level comment thread
        Paper,
        MockTest,
    }

    // One row in the unified "My Bookmarks" list, whatever type it actually is. Only the fields
    // relevant to Type are populated -- the rest stay at their default (null/0).
    public class BookmarkListItemDto
    {
        public Guid BookmarkId { get; set; }
        public BookmarkType Type { get; set; }
        public DateTime CreatedAt { get; set; }

        // The underlying content's own id -- e.g. for Type == Question this is the QuestionId, so
        // the frontend can route straight to /questions/{id} (or the equivalent for each type)
        // without a second lookup.
        public Guid TargetId { get; set; }

        // Question / QuestionBankQuestion
        public string? QuestionText { get; set; }
        public string? Subject { get; set; }

        // Discussion
        public string? CommentText { get; set; }
        public string? AuthorName { get; set; }
        public int? ReplyCount { get; set; }
        // The question this thread is attached to, so the frontend can deep-link straight into
        // QuestionDetail/QuestionBankQuestionDetail (there's no standalone single-thread page --
        // CommentThread only ever renders embedded in one of those two). Exactly one is set.
        public Guid? DiscussionQuestionId { get; set; }
        public Guid? DiscussionQuestionBankQuestionId { get; set; }

        // Paper
        public string? ExamName { get; set; }
        public int? Year { get; set; }
        public string? PaperCode { get; set; }

        // MockTest
        public string? Title { get; set; }
        public int? DurationMinutes { get; set; }
    }
}
