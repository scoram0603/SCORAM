namespace ScoramAPI.DTOs
{
    // One parsed row, before or after validation. RowNumber is 1-based and refers to the row's
    // position in the uploaded file (header row excluded) -- this is what the frontend uses to let an
    // admin pick exactly which rows to commit.
    public class ImportedQuestionRow
    {
        public int RowNumber { get; set; }
        public int QuestionNumber { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public string DifficultyLevel { get; set; } = "Medium";
        public string QuestionText { get; set; } = string.Empty;
        public string OptionA { get; set; } = string.Empty;
        public string OptionB { get; set; } = string.Empty;
        public string OptionC { get; set; } = string.Empty;
        public string OptionD { get; set; } = string.Empty;
        public string CorrectOption { get; set; } = string.Empty;
        public string? Explanation { get; set; }
        public string? SourceReference { get; set; }

        public bool IsValid { get; set; } = true;
        public List<string> Errors { get; set; } = new();
    }

    public class BulkImportPreviewResponseDto
    {
        public Guid JobId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public int TotalRows { get; set; }
        public int ValidCount { get; set; }
        public int InvalidCount { get; set; }
        public List<ImportedQuestionRow> Rows { get; set; } = new();
    }

    // RowNumbers omitted or null = commit every valid row. Provide an explicit subset for a partial
    // import (e.g. the admin unchecked a couple of valid-but-unwanted rows in the preview).
    public class BulkImportCommitDto
    {
        public List<int>? RowNumbers { get; set; }
    }

    public class BulkImportCommitResultDto
    {
        public Guid JobId { get; set; }
        public string Status { get; set; } = string.Empty;
        public int ImportedCount { get; set; }
        public int SkippedCount { get; set; }
    }

    public class ImportJobResponseDto
    {
        public Guid Id { get; set; }
        public Guid PaperId { get; set; }
        public string ExamName { get; set; } = string.Empty;
        public int Year { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int TotalRows { get; set; }
        public int ValidRows { get; set; }
        public int InvalidRows { get; set; }
        public int ImportedCount { get; set; }
        public string CreatedByAdminName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? CommittedAt { get; set; }
        public DateTime? RolledBackAt { get; set; }
    }
}
