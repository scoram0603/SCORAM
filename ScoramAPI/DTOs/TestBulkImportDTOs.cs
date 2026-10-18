namespace ScoramAPI.DTOs
{
    // ==================================================================================
    // Bulk upload of Mock Tests / Practice Test templates (JSON / Excel / CSV).
    // One shape for both kinds: a "test" has settings, optional sections, and questions. Practice templates
    // without questions are filter-based; with questions they are curated (a fixed paper).
    // The parsed + validated tests are cached between Preview and Commit (IStagedDataCache), so every
    // property here must serialize cleanly -- plain strings/numbers, no entities.
    // ==================================================================================

    public class TestImportQuestionDto
    {
        public int RowNumber { get; set; }
        // Optional: id of an existing Question Bank question to use as-is.
        public string? Ref { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string OptionA { get; set; } = string.Empty;
        public string OptionB { get; set; } = string.Empty;
        public string OptionC { get; set; } = string.Empty;
        public string OptionD { get; set; } = string.Empty;
        public string CorrectOption { get; set; } = string.Empty;
        public string? Explanation { get; set; }
        public string? Subject { get; set; }
        public string? Topic { get; set; }
        public string? Difficulty { get; set; }
        // Mock tests with sections: the section's name (matches TestImportSectionDto.Name, case-insensitive).
        public string? Section { get; set; }

        // ---- filled by validation ----
        // "Existing" = uses a question already in the Question Bank, "New" = created as a test-only question.
        public string? Resolution { get; set; }
        public Guid? ResolvedQuestionId { get; set; }
        public string? ResolvedKey { get; set; }
        public int SectionOrder { get; set; }
    }

    public class TestImportSectionDto
    {
        public string Name { get; set; } = string.Empty;
        public int? DurationMinutes { get; set; }
    }

    public class TestImportTestDto
    {
        public int Index { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Exam { get; set; }
        public string? TestType { get; set; }
        public int? DurationMinutes { get; set; }
        public string? NegativeMarking { get; set; }
        public string? Language { get; set; }
        // Dates are written like "2026-11-05 10:00" and stored as typed (same as the manual form).
        public string? ScheduledAt { get; set; }
        public string? EndAt { get; set; }
        public string? Status { get; set; }
        public int? MaxAttempts { get; set; }
        public string? Instructions { get; set; }
        public bool? RandomOrder { get; set; }
        public bool? ShuffleOptions { get; set; }
        public string? ResultRelease { get; set; }
        public int? ResultDelayMinutes { get; set; }
        public string? ResultReleaseAt { get; set; }

        // Practice templates only
        public string? Subject { get; set; }
        public string? Topic { get; set; }
        public int? YearFrom { get; set; }
        public int? YearTo { get; set; }
        public string? Difficulty { get; set; }
        public int? QuestionCount { get; set; }

        public List<TestImportSectionDto> Sections { get; set; } = new();
        public List<TestImportQuestionDto> Questions { get; set; } = new();

        // ---- filled by validation ----
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public Guid? ExamId { get; set; }
        public string? ExamName { get; set; }
        public Guid? SubjectId { get; set; }
        public Guid? TopicId { get; set; }
        public decimal NegativeMarkingValue { get; set; }
        public DateTime? ScheduledAtUtc { get; set; }
        public DateTime? EndAtUtc { get; set; }
        public DateTime? ResultReleaseAtUtc { get; set; }
        public int TotalMinutes { get; set; }
    }

    // What sits in the cache between Preview and Commit.
    public class TestImportStaged
    {
        public Guid JobId { get; set; }
        public Guid AdminId { get; set; }
        public string Kind { get; set; } = string.Empty;
        public List<TestImportTestDto> Tests { get; set; } = new();
    }

    public class TestImportPreviewTestDto
    {
        public int Index { get; set; }
        public string Title { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public string? Exam { get; set; }
        public int QuestionCount { get; set; }
        public int NewQuestions { get; set; }
        public int ExistingQuestions { get; set; }
        public int TotalMinutes { get; set; }
        public string? Status { get; set; }
        public string? ResultRelease { get; set; }
        public bool IsCurated { get; set; }
        public List<TestImportPreviewSectionDto> Sections { get; set; } = new();
    }

    public class TestImportPreviewSectionDto
    {
        public string Name { get; set; } = string.Empty;
        public int? DurationMinutes { get; set; }
        public int QuestionCount { get; set; }
    }

    public class TestImportPreviewDto
    {
        public string Token { get; set; } = string.Empty;
        public Guid JobId { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int TotalTests { get; set; }
        public int ValidTests { get; set; }
        public int InvalidTests { get; set; }
        public int TotalNewQuestions { get; set; }
        public int TotalExistingQuestions { get; set; }
        public List<string> FileErrors { get; set; } = new();
        public List<TestImportPreviewTestDto> Tests { get; set; } = new();
    }

    public class TestImportCommitDto
    {
        public string Token { get; set; } = string.Empty;
        // Indexes (TestImportPreviewTestDto.Index) to import; null/empty = every valid test.
        public List<int>? SelectedIndexes { get; set; }
    }

    public class TestImportCommitResultDto
    {
        public Guid JobId { get; set; }
        public int ImportedTests { get; set; }
        public int NewQuestionsCreated { get; set; }
        public int ExistingQuestionsReused { get; set; }
    }

    public class TestImportJobDto
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int TotalTests { get; set; }
        public int ValidTests { get; set; }
        public int InvalidTests { get; set; }
        public int ImportedTests { get; set; }
        public int NewQuestionsCreated { get; set; }
        public int ExistingQuestionsReused { get; set; }
        public string? CreatedByAdminName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CommittedAt { get; set; }
        public DateTime? RolledBackAt { get; set; }
        public List<TestImportJobTestDto>? Tests { get; set; }
    }

    public class TestImportJobTestDto
    {
        public Guid Id { get; set; }
        public string? BusinessId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int QuestionCount { get; set; }
    }

    public class TestImportRollbackResultDto
    {
        public int TestsRemoved { get; set; }
        public int QuestionsRemoved { get; set; }
        public int QuestionsKept { get; set; }
    }
}
