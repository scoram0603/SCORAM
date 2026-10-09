namespace ScoramAPI.DTOs
{
    // ---------- Step 1: paper selector ----------
    public class BulkPaperRowDto
    {
        public Guid Id { get; set; }
        public string ExamName { get; set; } = string.Empty;
        public int Year { get; set; }
        public string Language { get; set; } = string.Empty;
        public string? PaperLabel { get; set; }
        public string Status { get; set; } = string.Empty;
        public int QuestionCount { get; set; }
        // false for Published papers: content edits are locked there (same rule as the question editor).
        public bool ContentEditable { get; set; }
    }

    // ---------- Step 2: question selector ----------
    public class BulkQuestionFilterDto
    {
        public string? Search { get; set; }
        public string? Subject { get; set; }
        public string? Topic { get; set; }
        public string? Stimulus { get; set; }     // "any" (default) | "with" | "without"
        public Guid? PaperId { get; set; }        // narrow to one of the selected papers
    }

    public class BulkQuestionRowDto
    {
        public Guid PaperId { get; set; }
        public string PaperName { get; set; } = string.Empty;
        public int? QuestionNumber { get; set; }
        public string Source { get; set; } = string.Empty;   // "Paper" | "QuestionBank"
        public Guid? QuestionId { get; set; }
        public Guid? LinkId { get; set; }
        public string TextPreview { get; set; } = string.Empty;
        public string? Subject { get; set; }
        public string? Topic { get; set; }
        public bool HasStimulus { get; set; }
        public string? StimulusBusinessId { get; set; }
        // true only for paper-owned questions of non-published papers.
        public bool ContentEditable { get; set; }
    }

    public class BulkFilterOptionsDto
    {
        public List<string> Subjects { get; set; } = new();
        public List<string> Topics { get; set; } = new();
    }

    // ---------- Operations ----------
    // Mode A (same value for every selected question). Null = leave unchanged.
    public class BulkFieldChangesDto
    {
        public string? Subject { get; set; }
        public string? Topic { get; set; }
        public string? DifficultyLevel { get; set; }   // Easy | Medium | Hard
        public string? Language { get; set; }
        public string? Explanation { get; set; }
        public bool ClearExplanation { get; set; }
    }

    // Mode B (question-by-question workspace). Null = unchanged. Explanation "" is rejected; use ClearExplanation.
    public class BulkQuestionEditDto
    {
        public Guid QuestionId { get; set; }
        public string? QuestionText { get; set; }
        public string? OptionA { get; set; }
        public string? OptionB { get; set; }
        public string? OptionC { get; set; }
        public string? OptionD { get; set; }
        public string? CorrectOption { get; set; }     // A | B | C | D
        public string? Explanation { get; set; }
        public bool ClearExplanation { get; set; }
        public string? Subject { get; set; }
        public string? Topic { get; set; }
        public string? DifficultyLevel { get; set; }
        // Null = unchanged, empty list = clear, otherwise replaces the question's rich-content blocks.
        public List<ContentBlockDto>? ContentBlocks { get; set; }
    }

    public class BulkOperationDto
    {
        // SetFields | SetImage | RemoveImage | IndividualEdits | AttachStimulus | ReplaceStimulus | RemoveStimulus
        public string Type { get; set; } = string.Empty;
        public BulkFieldChangesDto? Fields { get; set; }
        public string? ImageField { get; set; }        // Question | OptionA | OptionB | OptionC | OptionD | Explanation
        public string? ImageUrl { get; set; }          // from POST bulk-question-editor/upload-image
        public Guid? StimulusId { get; set; }          // Attach (required) / Remove (optional: null = remove ALL stimuli)
        public Guid? FromStimulusId { get; set; }      // Replace
        public Guid? ToStimulusId { get; set; }        // Replace
        public List<BulkQuestionEditDto>? Edits { get; set; }
    }

    public class BulkRequestDto
    {
        public List<Guid> PaperIds { get; set; } = new();
        // Explicit selection (what was ticked). Ignored for IndividualEdits (the edits themselves are the selection).
        public List<StimulusTargetDto> Targets { get; set; } = new();
        // "Select all matching": the server re-runs this filter. ExpectedCount (what the admin confirmed) must still match.
        public BulkQuestionFilterDto? AllMatching { get; set; }
        public int? ExpectedCount { get; set; }
        public BulkOperationDto Operation { get; set; } = new();
    }

    // ---------- Results ----------
    public class BulkChangeCountDto
    {
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class BulkPreviewResultDto
    {
        public bool CanApply { get; set; }
        public int PaperCount { get; set; }
        public int QuestionCount { get; set; }
        public int ChangedCount { get; set; }
        public int UnchangedCount { get; set; }
        public string Operation { get; set; } = string.Empty;
        public string? StimulusLabel { get; set; }
        public List<BulkChangeCountDto> Changes { get; set; } = new();
        public List<string> Warnings { get; set; } = new();     // destructive-operation notices
        public int ErrorCount { get; set; }
        public List<string> Errors { get; set; } = new();       // first 50; any error blocks Apply
    }

    public class BulkApplyResultDto
    {
        public bool Success { get; set; }
        public int QuestionsChanged { get; set; }
        public string Message { get; set; } = string.Empty;
        public int ErrorCount { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
