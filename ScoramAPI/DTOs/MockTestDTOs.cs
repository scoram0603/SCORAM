using ScoramAPI.Enums;

namespace ScoramAPI.DTOs
{
    // A question reference that can point at either the legacy Question table or a Question Bank
    // question -- exactly one of the two should be set. Used wherever admin picks questions for a
    // MockTest or PracticeTestTemplate (SCORAM_TESTS' Question Bank integration).
    public class TestQuestionRefDto
    {
        public Guid? QuestionId { get; set; }
        public Guid? QuestionBankQuestionId { get; set; }
    }

    // PATCH .../status body, shared by MockTestsAdminController and PracticeTestsAdminController.
    public class UpdateTestStatusDto
    {
        public string Status { get; set; } = string.Empty; // Draft | Published | Archived
    }

    public class MockTestCreateDto
    {
        public string Title { get; set; } = string.Empty;
        public string ExamName { get; set; } = string.Empty;
        public MockTestType TestType { get; set; } = MockTestType.FullMockTest;
        public int DurationMinutes { get; set; }
        public decimal NegativeMarkingRatio { get; set; } = 0.25m;
        public bool IsRandomOrder { get; set; }
        public bool IsShuffleOptions { get; set; }
        public DateTime? ScheduledAt { get; set; }
        public DateTime? EndAt { get; set; }
        public int? MaxAttempts { get; set; }
        public string? Instructions { get; set; }
        public string Status { get; set; } = "Draft"; // Draft | Published | Archived

        // Legacy-only shape, kept working exactly as before for any existing caller.
        public List<Guid> QuestionIds { get; set; } = new();

        // Preferred going forward -- supports mixing in Question Bank questions. If both this and
        // QuestionIds are provided, the two lists are combined (QuestionIds first, in order).
        public List<TestQuestionRefDto> QuestionRefs { get; set; } = new();
    }

    public class MockTestUpdateDto : MockTestCreateDto
    {
    }

    public class MockTestSummaryDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ExamName { get; set; } = string.Empty;
        public string TestType { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
        public decimal NegativeMarkingRatio { get; set; }
        public int QuestionCount { get; set; }
        // Added for the Pre-Exam Instructions screen -- was already on the MockTest model/
        // MockTestDetailDto, just never surfaced on the summary before. Avoids that screen having to
        // call GetById (which also eagerly loads and ships all 100+ questions' full text/options,
        // unnecessary for a metadata-only briefing page -- see PreExamInstructions.jsx).
        public string? Instructions { get; set; }
        public DateTime? ScheduledAt { get; set; }
        public DateTime? EndAt { get; set; }
        public string Status { get; set; } = string.Empty;
        // Computed from ScheduledAt/EndAt/Status -- "Upcoming" | "Live" | "Completed" | "Draft" |
        // "Archived" (spec section "Mock Test Scheduling"). Draft/Archived take priority over the
        // date-derived states since they mean "not currently attemptable" regardless of the window.
        public string AvailabilityStatus { get; set; } = string.Empty;
        public int? MaxAttempts { get; set; }
        // How many attempts THIS student has already used, if authenticated -- null for anonymous.
        public int? MyAttemptCount { get; set; }
        // Whether the current viewer has this mock test bookmarked (false when not logged in).
        public bool IsBookmarked { get; set; }
    }

    // Question shape WITHOUT CorrectOption/Explanation -- this is what a student
    // sees while attempting the test. Never send the answer key before submission.
    public class MockTestQuestionDto
    {
        public Guid QuestionId { get; set; }
        public int QuestionOrder { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string OptionA { get; set; } = string.Empty;
        public string OptionB { get; set; } = string.Empty;
        public string OptionC { get; set; } = string.Empty;
        public string OptionD { get; set; } = string.Empty;
    }

    public class MockTestDetailDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ExamName { get; set; } = string.Empty;
        public string TestType { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
        public decimal NegativeMarkingRatio { get; set; }
        public bool IsRandomOrder { get; set; }
        public string? Instructions { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? ScheduledAt { get; set; }
        public DateTime? EndAt { get; set; }
        public int? MaxAttempts { get; set; }
        public List<MockTestQuestionDto> Questions { get; set; } = new();
    }

    public class AnswerSubmitDto
    {
        public Guid QuestionId { get; set; }
        // Null = the student skipped this question
        public OptionLetter? SelectedOption { get; set; }
    }

    public class MockTestSubmitDto
    {
        public List<AnswerSubmitDto> Answers { get; set; } = new();
        public int TimeTakenSeconds { get; set; }
    }

    // Per-question breakdown shown AFTER submission -- answer key is safe to reveal now.
    public class ResultQuestionDto
    {
        public Guid QuestionId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string OptionA { get; set; } = string.Empty;
        public string OptionB { get; set; } = string.Empty;
        public string OptionC { get; set; } = string.Empty;
        public string OptionD { get; set; } = string.Empty;
        public string? SelectedOption { get; set; }
        public string CorrectOption { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
        public string? Explanation { get; set; }
    }

    public class MockTestResultDto
    {
        public Guid AttemptId { get; set; }
        public Guid MockTestId { get; set; }
        public string MockTestTitle { get; set; } = string.Empty;
        public decimal Score { get; set; }
        public int MaxPossibleScore { get; set; }
        public int CorrectCount { get; set; }
        public int WrongCount { get; set; }
        public int SkippedCount { get; set; }
        public decimal AccuracyPercent { get; set; }
        public int TimeTakenSeconds { get; set; }
        public DateTime AttemptedAt { get; set; }
        public List<ResultQuestionDto> Questions { get; set; } = new();
    }

    // Lightweight row for "my past attempts" / Recent Tests list -- no per-question detail.
    public class AttemptSummaryDto
    {
        public Guid AttemptId { get; set; }
        public string MockTestTitle { get; set; } = string.Empty;
        public string ExamName { get; set; } = string.Empty;
        public decimal Score { get; set; }
        public int MaxPossibleScore { get; set; }
        public decimal AccuracyPercent { get; set; }
        public int TimeTakenSeconds { get; set; }
        public DateTime AttemptedAt { get; set; }
    }
}
