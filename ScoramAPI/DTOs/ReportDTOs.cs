namespace ScoramAPI.DTOs
{
    // Student submission -- POST /api/questions/{id}/reports or /api/question-bank/{id}/reports.
    public class QuestionReportCreateDto
    {
        public string ReportType { get; set; } = string.Empty; // WrongAnswer, WrongOption, WrongQuestionStatement,
                                                                 // IncorrectExplanation, TypingMistake, Duplicate,
                                                                 // IncorrectExamYear, Other
        public string? Description { get; set; }
        public string? ProofUrl { get; set; }
    }

    public class QuestionReportResponseDto
    {
        public Guid Id { get; set; }
        public Guid? QuestionId { get; set; }
        public Guid? QuestionBankQuestionId { get; set; }
        public string ReportType { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    // Admin moderation queue row -- enough question context to review without a second round trip,
    // same idea as PendingSolutionDto.
    public class AdminQuestionReportDto
    {
        public Guid Id { get; set; }
        public Guid? QuestionId { get; set; }
        public Guid? QuestionBankQuestionId { get; set; }
        public bool IsQuestionBank { get; set; }
        public string QuestionTextSnippet { get; set; } = string.Empty;
        public string ContextLabel { get; set; } = string.Empty; // exam name (legacy) or "Subject / Topic" (bank)
        public string ReportType { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ProofUrl { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ReportedByName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }

    // PATCH /api/admin/reports/{id}/status
    public class UpdateReportStatusDto
    {
        public string Status { get; set; } = string.Empty; // UnderReview, Resolved, Rejected
    }
}
