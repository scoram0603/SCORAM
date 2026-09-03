namespace ScoramAPI.DTOs
{
    // One row parsed from a bulk "paper shells" CSV/Excel upload -- see BulkPaperImportService and
    // BulkPaperImportController. Deliberately much simpler than ImportedQuestionRow
    // (BulkImportDTOs.cs): a paper shell never carries images or question content, just its identity
    // fields, created Draft with zero questions -- the admin fills in Duration/NegativeMarkingRatio/
    // RequiredQuestionCount and adds questions afterward through the normal PaperDetailView flow,
    // exactly like a paper created one at a time through the wizard.
    public class ImportedPaperRow
    {
        public int RowNumber { get; set; }
        public string ExamName { get; set; } = string.Empty;
        public int Year { get; set; }

        // All three optional -- an exam with no tiers/shifts/a paper with no fixed date just leaves
        // these blank, same as PaperCreateDto.
        public string? Tier { get; set; }
        public string? Shift { get; set; }
        public string? ExamDateRaw { get; set; } // raw text as typed, kept so a bad date shows the admin exactly what they entered instead of just "invalid"
        public DateOnly? ExamDate { get; set; }

        public string Medium { get; set; } = string.Empty; // raw text ("Hindi"/"English") before PaperLanguage parsing
        public string? PaperCode { get; set; }
        public string? PaperLabel { get; set; }

        public bool IsValid { get; set; } = true;
        public List<string> Errors { get; set; } = new();

        // Preview-only hints computed by BulkPaperImportService.ValidateAsync, so the admin can see
        // -- before committing -- whether this row's exam already exists or a brand-new one will be
        // created for it, and whether an identical paper already exists. IsValid stays true for an
        // already-existing paper (it's a perfectly well-formed row); Commit just skips it rather than
        // erroring the whole batch, the same tolerant-of-partial-overlap behavior Rollback/Commit use
        // elsewhere in the bulk-question-import flow.
        public bool ExamExists { get; set; }
        public bool PaperAlreadyExists { get; set; }
    }

    public class BulkPaperImportPreviewResponseDto
    {
        public Guid JobId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public int TotalRows { get; set; }
        public int ValidCount { get; set; } // valid AND not already existing -- i.e. what Commit creates by default
        public int InvalidCount { get; set; }
        public int AlreadyExistsCount { get; set; }
        public List<ImportedPaperRow> Rows { get; set; } = new();
    }

    // POST /api/admin/bulk-papers/{jobId}/commit -- RowNumbers null means "every valid, not-already-
    // existing row" (the common case); otherwise only the given row numbers are attempted, each
    // still re-validated server-side against the live database (not just trusted from Preview) --
    // another admin could have created a colliding paper in the few minutes since Preview ran.
    public class BulkPaperImportCommitDto
    {
        public List<int>? RowNumbers { get; set; }
    }

    public class BulkPaperImportCommitResultDto
    {
        public int CreatedCount { get; set; }
        public int SkippedExistingCount { get; set; }
        public List<PaperResponseDto> CreatedPapers { get; set; } = new();
    }
}
