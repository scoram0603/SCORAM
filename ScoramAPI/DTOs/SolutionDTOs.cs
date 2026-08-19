using ScoramAPI.Enums;

namespace ScoramAPI.DTOs
{
    public class SolutionCreateDto
    {
        public string Title { get; set; } = string.Empty;
        public SolutionType SolutionType { get; set; } = SolutionType.Community;
        public string SolutionText { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
    }

    public class SolutionResponseDto
    {
        public Guid Id { get; set; }
        // Exactly one of these two is set (SCORAM_QUESTION_BANK -- see Models/QuestionModels.cs).
        public Guid? QuestionId { get; set; }
        public Guid? QuestionBankQuestionId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string SolutionType { get; set; } = string.Empty;
        public string SolutionText { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string SubmittedByName { get; set; } = string.Empty;
        public bool SubmittedByAdmin { get; set; }
        public int Priority { get; set; }
        public int UpvoteCount { get; set; }
        public bool IsVerified { get; set; }
        public bool IsEasiestMethod { get; set; }
        // True for every solution a student sees except their own still-pending submission (see
        // SolutionsController.ListForQuestion) -- the frontend uses this to show a "pending review"
        // badge on a student's own freshly-submitted solution rather than hiding it from them entirely.
        public bool IsApproved { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // What an admin sees in the moderation queue -- same solution fields, plus enough question
    // context (via QuestionId) to link straight to it without a second round trip.
    public class PendingSolutionDto
    {
        public Guid Id { get; set; }
        // Exactly one of these two is set -- the frontend uses IsQuestionBank to decide which detail
        // page to link to (/questions/{id} vs /question-bank/{id}).
        public Guid? QuestionId { get; set; }
        public Guid? QuestionBankQuestionId { get; set; }
        public bool IsQuestionBank { get; set; }
        public string QuestionTextSnippet { get; set; } = string.Empty;
        public string ExamName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string SolutionType { get; set; } = string.Empty;
        public string SolutionText { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public string SubmittedByName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
