namespace ScoramAPI.DTOs
{
    public class CommentCreateDto
    {
        public string CommentText { get; set; } = string.Empty;
    }

    public class CommentResponseDto
    {
        public Guid Id { get; set; }
        // Exactly one of these two is set (SCORAM_QUESTION_BANK) -- see Models/QuestionModels.cs.
        public Guid? QuestionId { get; set; }
        public Guid? QuestionBankQuestionId { get; set; }
        public Guid? ParentCommentId { get; set; }
        public string CommentText { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public bool AuthorIsAdmin { get; set; }
        public int UpvoteCount { get; set; }
        public int DownvoteCount { get; set; }
        // true = I upvoted, false = I downvoted, null = I haven't voted (or I'm not logged in).
        // Drives which of the up/down arrows renders "active" in the UI.
        public bool? MyVote { get; set; }
        public bool IsPinned { get; set; }
        public bool IsAdminHighlighted { get; set; }
        public bool IsResolved { get; set; }
        // True only for the comment's own author viewing their own top-level comment -- the frontend
        // uses this to decide whether to show the "mark solved" toggle at all (there's no point
        // showing a control that would just 403 for everyone except the asker).
        public bool IsMine { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<CommentResponseDto> Replies { get; set; } = new();
    }

    public class CommentReportCreateDto
    {
        public string? Reason { get; set; }
    }

    // What an admin sees in the reported-comments queue -- the comment itself plus who reported it
    // and why, with enough question context to jump straight to the thread.
    public class ReportedCommentDto
    {
        public Guid ReportId { get; set; }
        public Guid CommentId { get; set; }
        public Guid? QuestionId { get; set; }
        public Guid? QuestionBankQuestionId { get; set; }
        public bool IsQuestionBank { get; set; }
        public string QuestionTextSnippet { get; set; } = string.Empty;
        public string CommentText { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public string ReportedByName { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // One row in the global "Top Discussions" feed (GET /api/discussions) --
    // shaped to match what the frontend's TopDiscussions component needs
    // (exam/subject chips, preview text) without a second round-trip per item.
    public class DiscussionFeedItemDto
    {
        public Guid CommentId { get; set; }
        public Guid QuestionId { get; set; }
        public string ExamName { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string CommentText { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public int UpvoteCount { get; set; }
        public int ReplyCount { get; set; }
        public DateTime CreatedAt { get; set; }
        // Whether the current viewer has this thread bookmarked (false when not logged in).
        public bool IsBookmarked { get; set; }
    }

    // Question-level Like/Dislike (distinct from a CommentVote on one reply -- see
    // Models/QuestionModels.cs's QuestionVote). One request/response shape shared by
    // POST /api/questions/{id}/vote and POST /api/question-bank/{id}/vote.
    public class QuestionVoteRequestDto
    {
        public bool IsLike { get; set; }
    }

    public class QuestionVoteResponseDto
    {
        public int LikeCount { get; set; }
        public int DislikeCount { get; set; }
        // true = I liked it, false = I disliked it, null = no vote from me.
        public bool? MyVote { get; set; }
    }
}
