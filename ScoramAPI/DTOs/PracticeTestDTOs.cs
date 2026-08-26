namespace ScoramAPI.DTOs
{
    // POST /api/practice-tests/generate -- the "ad-hoc" flow (spec section A): a student picks these
    // filters themselves and gets a fresh attempt with no PracticeTestTemplate involved at all.
    public class PracticeTestGenerateDto
    {
        public Guid? SubjectId { get; set; }
        public Guid? TopicId { get; set; }
        public Guid? ExamId { get; set; }
        public int? YearFrom { get; set; }
        public int? YearTo { get; set; }
        public string? Difficulty { get; set; } // Easy | Medium | Hard, null = any
        // "Hindi" | "English", optional -- see QuestionBankQuestion.Language.
        public string? Language { get; set; }
        public int QuestionCount { get; set; } = 20;
        public int DurationMinutes { get; set; } = 20;
        public decimal NegativeMarkingRatio { get; set; } = 0m;
        public bool IsRandomOrder { get; set; } = true;
    }

    // Student-facing browsable list entry (spec section B: admin-curated templates).
    public class PracticeTestTemplateDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Subject { get; set; }
        public string? Topic { get; set; }
        public string? ExamName { get; set; }
        public int? YearFrom { get; set; }
        public int? YearTo { get; set; }
        public string? Difficulty { get; set; }
        public int QuestionCount { get; set; }
        public int DurationMinutes { get; set; }
        public decimal NegativeMarkingRatio { get; set; }
        public bool IsCurated { get; set; } // true = fixed question list; false = generated fresh from filters each attempt
    }

    // Admin create/edit -- either leave Questions empty (FilterBased: pool is assembled fresh from
    // the filter criteria on every attempt) or provide a fixed list (Curated: always these exact
    // questions, in this order unless IsRandomOrder shuffles display order).
    public class PracticeTestTemplateCreateDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid? SubjectId { get; set; }
        public Guid? TopicId { get; set; }
        public Guid? ExamId { get; set; }
        public int? YearFrom { get; set; }
        public int? YearTo { get; set; }
        public string? Difficulty { get; set; }
        public int QuestionCount { get; set; } = 20;
        public int DurationMinutes { get; set; } = 20;
        public decimal NegativeMarkingRatio { get; set; } = 0m;
        public bool IsRandomOrder { get; set; } = true;
        public string Status { get; set; } = "Draft"; // Draft | Published | Archived
        public List<TestQuestionRefDto> Questions { get; set; } = new(); // empty = FilterBased
    }

    public class PracticeTestTemplateAdminDto : PracticeTestTemplateDto
    {
        public string Status { get; set; } = string.Empty;
        public string CreatedByAdminName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int AttemptCount { get; set; }
    }
}
