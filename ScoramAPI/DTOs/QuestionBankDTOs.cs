namespace ScoramAPI.DTOs
{
    // ---------- Subject / Topic ----------

    public class QuestionBankSubjectDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int QuestionCount { get; set; }
    }

    public class QuestionBankSubjectCreateDto
    {
        public string Name { get; set; } = string.Empty;
    }

    public class QuestionBankTopicDto
    {
        public Guid Id { get; set; }
        public Guid SubjectId { get; set; }
        public string SubjectName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int QuestionCount { get; set; }
    }

    public class QuestionBankTopicCreateDto
    {
        public Guid SubjectId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    // ---------- Exam/Year mapping ----------

    public class QuestionBankExamYearDto
    {
        public Guid ExamId { get; set; }
        public string ExamName { get; set; } = string.Empty;
        public string? ExamLogoUrl { get; set; }
        public int Year { get; set; }
    }

    // Used on create/update -- admin sends exam name (existing exam picked from the dropdown, or a
    // brand-new name to auto-create) + year for each appearance.
    public class QuestionBankExamYearInputDto
    {
        public Guid? ExamId { get; set; }
        public string? ExamName { get; set; }
        public int Year { get; set; }
    }

    // ---------- Question ----------

    public class QuestionBankQuestionCreateDto
    {
        public string QuestionText { get; set; } = string.Empty;
        public string OptionA { get; set; } = string.Empty;
        public string OptionB { get; set; } = string.Empty;
        public string OptionC { get; set; } = string.Empty;
        public string OptionD { get; set; } = string.Empty;
        public string CorrectOption { get; set; } = string.Empty; // "A"/"B"/"C"/"D"
        public string? Explanation { get; set; }
        public Guid SubjectId { get; set; }
        public Guid TopicId { get; set; }
        public string? SourceReference { get; set; }
        // "Hindi" | "English", case-insensitive -- null/omitted stays unset, same as SourceReference.
        public string? Language { get; set; }
        public List<QuestionBankExamYearInputDto> ExamYears { get; set; } = new();

        // Optional JSON array of { type, content } blocks -- see DTOs/ContentBlockDto.cs. Mirrors
        // QuestionCreateDto.ContentBlocksJson exactly (Create/Update here are plain JSON bodies, not
        // multipart, but the field's shape and validation are identical).
        public string? ContentBlocksJson { get; set; }

        // If true and a near-identical question already exists, the API still creates this as a
        // separate row instead of returning 409 Conflict -- used only after an admin has explicitly
        // reviewed the "Duplicate Question Found" prompt and chosen "Create anyway".
        public bool ConfirmCreateDespiteDuplicate { get; set; }
    }

    public class QuestionBankQuestionUpdateDto
    {
        public string QuestionText { get; set; } = string.Empty;
        public string OptionA { get; set; } = string.Empty;
        public string OptionB { get; set; } = string.Empty;
        public string OptionC { get; set; } = string.Empty;
        public string OptionD { get; set; } = string.Empty;
        public string CorrectOption { get; set; } = string.Empty;
        public string? Explanation { get; set; }
        public Guid SubjectId { get; set; }
        public Guid TopicId { get; set; }
        public string? SourceReference { get; set; }
        public string? Language { get; set; }
        public List<QuestionBankExamYearInputDto> ExamYears { get; set; } = new();
        public string? ContentBlocksJson { get; set; }
    }

    // Student-facing -- deliberately omits internal fields (ImportJobId, CreatedByAdminId, etc, per
    // section 4: "Do NOT expose unnecessary database/internal fields to students").
    public class QuestionBankQuestionResponseDto
    {
        public Guid Id { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string? QuestionImageUrl { get; set; }
        public string OptionA { get; set; } = string.Empty;
        public string? OptionAImageUrl { get; set; }
        public string OptionB { get; set; } = string.Empty;
        public string? OptionBImageUrl { get; set; }
        public string OptionC { get; set; } = string.Empty;
        public string? OptionCImageUrl { get; set; }
        public string OptionD { get; set; } = string.Empty;
        public string? OptionDImageUrl { get; set; }
        public string CorrectOption { get; set; } = string.Empty;
        public string? Explanation { get; set; }
        public string? ExplanationImageUrl { get; set; }
        // Optional rich-content sequence -- see DTOs/ContentBlockDto.cs. Empty for every question
        // that doesn't use this feature.
        public List<ContentBlockDto> ContentBlocks { get; set; } = new();
        public string Subject { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public string? SourceReference { get; set; }
        // "Hindi" | "English" | null (not specified) -- see the model's own comment on why this can
        // be null for older rows.
        public string? Language { get; set; }
        public List<QuestionBankExamYearDto> AskedIn { get; set; } = new();
        public int SolutionCount { get; set; }
        public int LikeCount { get; set; }
        public int DislikeCount { get; set; }
        // true = I liked it, false = I disliked it, null = no vote from me (or not logged in).
        public bool? MyVote { get; set; }
        public int CommentCount { get; set; }
        // Whether the current viewer has this question bookmarked (false when not logged in).
        public bool IsBookmarked { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // What an admin sees in the management list/table -- same shape plus the fields a student never
    // needs (who created it, when it was last edited, which import it came from).
    public class QuestionBankAdminQuestionDto : QuestionBankQuestionResponseDto
    {
        public Guid SubjectId { get; set; }
        public Guid TopicId { get; set; }
        public string CreatedByAdminName { get; set; } = string.Empty;
        public Guid? ImportJobId { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    // POST /api/admin/question-bank/{id}/images (multipart/form-data) -- set/replace/remove each of
    // the 6 images independently, same RemoveXImage-flag pattern as QuestionUpdateDto. Split out from
    // Create/Update (which stay plain JSON) rather than converting those to multipart -- the JSON
    // create/edit flow already works and this keeps that untouched; images are attached as a second
    // step right after (single-add) or whenever an admin opens Edit (including a bulk-imported
    // question, which never had images at all before this).
    public class QuestionBankImagesUpdateDto
    {
        public Microsoft.AspNetCore.Http.IFormFile? QuestionImage { get; set; }
        public bool RemoveQuestionImage { get; set; }
        public Microsoft.AspNetCore.Http.IFormFile? OptionAImage { get; set; }
        public bool RemoveOptionAImage { get; set; }
        public Microsoft.AspNetCore.Http.IFormFile? OptionBImage { get; set; }
        public bool RemoveOptionBImage { get; set; }
        public Microsoft.AspNetCore.Http.IFormFile? OptionCImage { get; set; }
        public bool RemoveOptionCImage { get; set; }
        public Microsoft.AspNetCore.Http.IFormFile? OptionDImage { get; set; }
        public bool RemoveOptionDImage { get; set; }
        public Microsoft.AspNetCore.Http.IFormFile? ExplanationImage { get; set; }
        public bool RemoveExplanationImage { get; set; }
    }

    // GET /api/question-bank/search query parameters
    public class QuestionBankSearchQuery
    {
        // Free-text: matches a keyword OR a fully-pasted question (partial match either way -- see
        // QuestionBankController.Search).
        public string? Search { get; set; }

        // Each filter accepts one OR several values -- e.g. ?examIds=guid1&examIds=guid2 matches
        // questions mapped to EITHER exam (OR within a filter). Selecting values across different
        // filters narrows the result (AND across filters) -- e.g. examIds=[SSC CGL, RRB NTPC] AND
        // subjectIds=[Reasoning] returns Reasoning questions from either exam. A student can leave
        // any filter empty (matches everything) or pick just one value -- multi-select doesn't force
        // multiple picks, see QuestionBankController.Search for the exact query-building.
        public List<Guid>? SubjectIds { get; set; }
        public List<Guid>? TopicIds { get; set; }
        public List<Guid>? ExamIds { get; set; }
        public List<int>? Years { get; set; }
        // "Hindi" | "English", one or several -- matches Language exactly (case-insensitive, see
        // Search's parsing).
        public List<string>? Languages { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    // ---------- Bulk import ----------

    // One parsed row from an Excel/JSON upload, before or after validation -- mirrors
    // ImportedQuestionRow's role for the Paper-based importer, adapted for Question Bank's shape
    // (Subject/Topic by name, multiple Exam+Year pairs, no QuestionNumber).
    public class QuestionBankImportRow
    {
        public int RowNumber { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string OptionA { get; set; } = string.Empty;
        public string OptionB { get; set; } = string.Empty;
        public string OptionC { get; set; } = string.Empty;
        public string OptionD { get; set; } = string.Empty;
        public string CorrectOption { get; set; } = string.Empty;
        public string? Explanation { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public string? SourceReference { get; set; }

        // Populated only for a ZIP upload (JSON/Excel/CSV rows leave these null) -- once an image
        // referenced by filename in the row is validated and staged, this holds the STAGED relative
        // "/uploads/bulk-import-staging/{jobId}/..." URL, not the original filename. Commit copies
        // the staged blob into the permanent "question-images" folder (see
        // IFileStorageService.CopyImageAsync) and deletes the staging copy; an expired/abandoned job
        // has its staging folder cleaned up the same way. See Services/QuestionBankImportService for
        // where these get set.
        public string? QuestionImageUrl { get; set; }
        public string? OptionAImageUrl { get; set; }
        public string? OptionBImageUrl { get; set; }
        public string? OptionCImageUrl { get; set; }
        public string? OptionDImageUrl { get; set; }
        public string? ExplanationImageUrl { get; set; }

        // Optional JSON array of { type, content } blocks -- see DTOs/ContentBlockDto.cs. Only
        // meaningful for JSON/ZIP uploads (a spreadsheet column holding a JSON blob is awkward, but
        // not disallowed -- ValidateAsync applies the same ContentBlocksJsonHelper validation either
        // way).
        public string? ContentBlocksJson { get; set; }

        // "Hindi" | "English" | "" (blank) -- optional per-row medium. A blank value is resolved to
        // the upload's DefaultLanguage (see Preview's "language" form field) during ValidateAsync,
        // so admins can either set the medium once for the whole batch OR override it row-by-row in
        // the file itself -- both are supported (see QuestionBankAdminController.Preview).
        public string? Language { get; set; }

        // Raw "ExamName:Year" pairs as typed in the file (e.g. "SSC CGL:2018; UP TGT:2022") --
        // parsed into structured pairs below once the row passes basic validation.
        public string RawExamYears { get; set; } = string.Empty;
        public List<QuestionBankExamYearInputDto> ExamYears { get; set; } = new();

        // Errors recorded while staging this row's images (ZIP upload only) -- same purpose and the
        // same "survives ValidateAsync's clear-and-recompute" contract as
        // ImportedQuestionRow.ImageErrors (see that field's own comment for why this can't just be
        // folded into Errors directly).
        public List<string> ImageErrors { get; set; } = new();

        public bool IsValid { get; set; } = true;
        public List<string> Errors { get; set; } = new();

        // Set during validation if an existing (or another in-batch) question normalizes to the same
        // text -- see section 13. A duplicate row can still be valid; it just gets merged instead of
        // creating a new question at commit time.
        public bool IsDuplicate { get; set; }
        public Guid? DuplicateOfQuestionId { get; set; }
        public string? DuplicateOfQuestionTextSnippet { get; set; }
    }

    public class QuestionBankImportPreviewResponseDto
    {
        public Guid JobId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public int TotalRows { get; set; }
        public int ValidCount { get; set; }
        public int InvalidCount { get; set; }
        public int DuplicateCount { get; set; }
        public List<QuestionBankImportRow> Rows { get; set; } = new();
    }

    public class QuestionBankImportCommitDto
    {
        // RowNumbers omitted/null = commit every valid row (new + merged-duplicate). Provide an
        // explicit subset to commit only some of them.
        public List<int>? RowNumbers { get; set; }
    }

    public class QuestionBankImportCommitResultDto
    {
        public Guid JobId { get; set; }
        public string Status { get; set; } = string.Empty;
        public int ImportedCount { get; set; }
        public int MergedIntoExistingCount { get; set; }
        public int SkippedCount { get; set; }
    }

    public class QuestionBankImportJobDto
    {
        public Guid Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int TotalRows { get; set; }
        public int ValidRows { get; set; }
        public int InvalidRows { get; set; }
        public int DuplicateRows { get; set; }
        public int ImportedCount { get; set; }
        public int MergedIntoExistingCount { get; set; }
        public string CreatedByAdminName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? CommittedAt { get; set; }
    }

    // ---------- Dashboard ----------

    public class QuestionBankStatsDto
    {
        public int TotalQuestions { get; set; }
        public int TotalSubjects { get; set; }
        public int TotalTopics { get; set; }
        public int TotalExamsUsed { get; set; }
        public int TotalDistinctYears { get; set; }
        public int QuestionsAddedToday { get; set; }
        public int QuestionsAddedThisMonth { get; set; }
        public int PendingReports { get; set; }
        public int PendingAlternativeSolutions { get; set; }
    }
}
