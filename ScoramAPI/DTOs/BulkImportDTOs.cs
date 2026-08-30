using Microsoft.AspNetCore.Http;

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

        // Populated only for a ZIP upload (CSV/Excel/JSON rows leave these null) -- see
        // QuestionBankImportRow's own comment on the staged-URL contract, which this mirrors exactly.
        public string? QuestionImageUrl { get; set; }
        public string? OptionAImageUrl { get; set; }
        public string? OptionBImageUrl { get; set; }
        public string? OptionCImageUrl { get; set; }
        public string? OptionDImageUrl { get; set; }
        public string? ExplanationImageUrl { get; set; }

        // Optional JSON array of { type, content } blocks -- see DTOs/ContentBlockDto.cs.
        public string? ContentBlocksJson { get; set; }

        // Errors recorded while staging this row's images (ZIP upload only) -- e.g. a referenced
        // filename wasn't found in the ZIP, or failed image validation. Kept separate from Errors
        // because BulkImportService.Validate() clears and fully recomputes Errors from scratch on
        // every call (including every PATCH re-validation of a text edit); folding these into Errors
        // directly would make them vanish the next time a row's text is corrected and re-validated.
        // Validate() seeds Errors from this list before adding its own text-validation errors.
        public List<string> ImageErrors { get; set; } = new();

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

    // POST .../bulk-import/{jobId}/rows/{rowNumber}/images -- multipart form for adding, replacing,
    // or removing one or more of a preview row's images before commit. Mirrors QuestionUpdateDto's
    // image fields exactly (same "file provided -> replace, flag set -> remove, neither -> leave
    // alone" contract) -- the only difference is these land in the bulk-import staging folder
    // instead of permanent storage, since the row isn't a real Question yet. Works for a row from
    // ANY format (CSV/Excel/JSON/ZIP), not just one that already came with a ZIP-staged image --
    // this is how a CSV/Excel/JSON row gets its first image at all, before commit.
    public class BulkImportRowImagesDto
    {
        public IFormFile? QuestionImage { get; set; }
        public bool RemoveQuestionImage { get; set; }
        public IFormFile? OptionAImage { get; set; }
        public bool RemoveOptionAImage { get; set; }
        public IFormFile? OptionBImage { get; set; }
        public bool RemoveOptionBImage { get; set; }
        public IFormFile? OptionCImage { get; set; }
        public bool RemoveOptionCImage { get; set; }
        public IFormFile? OptionDImage { get; set; }
        public bool RemoveOptionDImage { get; set; }
        public IFormFile? ExplanationImage { get; set; }
        public bool RemoveExplanationImage { get; set; }
    }
}
