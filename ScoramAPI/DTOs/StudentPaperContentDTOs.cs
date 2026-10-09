namespace ScoramAPI.DTOs
{
    // Student-safe view of a Shared Stimulus: no admin names, no description, no status internals.
    // Sent ONCE per paper attempt; questions only carry the ids (TestAttemptQuestionDto.StimulusIds),
    // so a passage shared by 5 questions is never duplicated in the payload.
    public class StudentStimulusDto
    {
        public Guid Id { get; set; }
        public string? BusinessId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Type { get; set; }
        public string? Language { get; set; }
        public List<ContentBlockDto> ContentBlocks { get; set; } = new();
    }

    public class StudentPaperInstructionDto
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public string? Language { get; set; }
        public int DisplayOrder { get; set; }
        public List<ContentBlockDto> ContentBlocks { get; set; } = new();
    }

    // ---------- Admin "Paper Preview" (what a student will see, in question-number order) ----------
    public class PaperContentPreviewQuestionDto
    {
        public int? QuestionNumber { get; set; }
        public string Source { get; set; } = string.Empty;     // "Paper" | "QuestionBank"
        public Guid? QuestionId { get; set; }
        public Guid? LinkId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string OptionA { get; set; } = string.Empty;
        public string OptionB { get; set; } = string.Empty;
        public string OptionC { get; set; } = string.Empty;
        public string OptionD { get; set; } = string.Empty;
        public string? QuestionImageUrl { get; set; }
        public string? OptionAImageUrl { get; set; }
        public string? OptionBImageUrl { get; set; }
        public string? OptionCImageUrl { get; set; }
        public string? OptionDImageUrl { get; set; }
        public List<ContentBlockDto> ContentBlocks { get; set; } = new();
        public List<Guid> StimulusIds { get; set; } = new();
    }

    public class PaperContentPreviewDto
    {
        public Guid PaperId { get; set; }
        public List<StudentPaperInstructionDto> Instructions { get; set; } = new();
        public List<StudentStimulusDto> Stimuli { get; set; } = new();
        public List<PaperContentPreviewQuestionDto> Questions { get; set; } = new();
    }
}
