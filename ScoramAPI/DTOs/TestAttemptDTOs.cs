namespace ScoramAPI.DTOs
{
    // ==================================================================================
    // SCORAM_TESTS -- shared attempt flow. Both Practice and Mock Tests produce the exact same
    // shapes here once an attempt exists; only how the attempt gets CREATED differs (see
    // MockTestsController.Start vs PracticeTestsController.Generate/StartFromTemplate).
    // ==================================================================================

    // What the student sees WHILE attempting -- never the answer key. Id is the StudentAnswer's own
    // Id, used as the target for auto-save (PATCH /api/tests/attempts/answers/{id}).
    public class TestAttemptQuestionDto
    {
        public Guid Id { get; set; }
        public int QuestionOrder { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string OptionA { get; set; } = string.Empty;
        public string OptionB { get; set; } = string.Empty;
        public string OptionC { get; set; } = string.Empty;
        public string OptionD { get; set; } = string.Empty;
        public string? SelectedOption { get; set; }
        public bool IsMarkedForReview { get; set; }
    }

    // Returned by both "start a Mock Test" and "generate/start a Practice Test" -- and again by
    // "resume" (GET), so the frontend's attempt-runner component doesn't need to know or care which
    // kind of test it's looking at.
    public class TestAttemptStartResponseDto
    {
        public Guid AttemptId { get; set; }
        public string TestKind { get; set; } = string.Empty; // "Practice" | "Mock"
        public string Title { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
        public decimal NegativeMarkingRatio { get; set; }
        public DateTime StartedAt { get; set; }
        // StartedAt + DurationMinutes -- the frontend timer counts down to this, not from a fresh
        // DurationMinutes every time, so a resumed attempt's clock picks up where it really left off.
        public DateTime ExpiresAt { get; set; }
        public string? Instructions { get; set; }
        public List<TestAttemptQuestionDto> Questions { get; set; } = new();
    }

    // PATCH /api/tests/attempts/answers/{studentAnswerId} -- auto-save, called on every answer
    // change/mark-for-review toggle. SelectedOption: null explicitly clears the answer ("Clear
    // Response"); the field is REQUIRED in the body (not omitted) specifically so "clear" and
    // "don't change" are distinguishable -- see TestAttemptsController.SaveAnswer.
    public class TestAnswerSaveDto
    {
        public string? SelectedOption { get; set; }
        public bool? IsMarkedForReview { get; set; }
    }

    public class TestAnswerSaveResponseDto
    {
        public Guid Id { get; set; }
        public string? SelectedOption { get; set; }
        public bool IsMarkedForReview { get; set; }
        public DateTime? AnsweredAt { get; set; }
    }

    // POST /api/tests/attempts/{attemptId}/submit
    public class TestSubmitDto
    {
        public int TimeTakenSeconds { get; set; }
    }

    // Full per-question breakdown shown after submission -- the answer key is safe to reveal now.
    // Carries enough to wire Report Question / Alternative Solution / Comments / Like straight from
    // the result screen, pointing at whichever LIVE question (legacy or Question Bank) this was
    // sourced from -- separate from the frozen QuestionText/Options/etc snapshot shown above it.
    public class TestAnswerReviewDto
    {
        public Guid StudentAnswerId { get; set; }
        public int QuestionOrder { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string OptionA { get; set; } = string.Empty;
        public string OptionB { get; set; } = string.Empty;
        public string OptionC { get; set; } = string.Empty;
        public string OptionD { get; set; } = string.Empty;
        public string CorrectOption { get; set; } = string.Empty;
        public string? SelectedOption { get; set; }
        public bool IsCorrect { get; set; }
        public bool WasSkipped { get; set; }
        public string? Explanation { get; set; }
        public string? Subject { get; set; }
        public string? Topic { get; set; }

        // Points at the live question for Report/Alternative Solution/Comments/Like -- reuses all
        // four features with zero new backend code (see Controllers/QuestionReportsController.cs,
        // SolutionsController.cs, DiscussionsController.cs, QuestionVotesController.cs), since every
        // one of them already accepts either a legacy Question id or a Question Bank question id.
        public Guid? SourceQuestionId { get; set; }
        public Guid? SourceQuestionBankQuestionId { get; set; }
        public bool IsQuestionBank { get; set; }
    }

    public class TestSubmitResultDto
    {
        public Guid AttemptId { get; set; }
        public string TestKind { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public decimal Score { get; set; }
        public int MaxPossibleScore { get; set; }
        public int TotalQuestions { get; set; }
        public int CorrectCount { get; set; }
        public int WrongCount { get; set; }
        public int SkippedCount { get; set; }
        public decimal AccuracyPercent { get; set; } // correct / (correct + wrong), excludes skipped
        public decimal PercentageScore { get; set; } // score / maxPossibleScore
        public int TimeTakenSeconds { get; set; }
        public int? Rank { get; set; }
        public int? Percentile { get; set; }
        public DateTime AttemptedAt { get; set; }
        public List<TestAnswerReviewDto> Questions { get; set; } = new();
    }

    // Lightweight row for "My Tests" -- In Progress / Completed, Practice + Mock mixed together
    // (spec: student navigation section).
    public class MyTestAttemptSummaryDto
    {
        public Guid AttemptId { get; set; }
        public string TestKind { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal? Score { get; set; }
        public decimal? PercentageScore { get; set; }
        public decimal? AccuracyPercent { get; set; }
        public int TimeTakenSeconds { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? SubmittedAt { get; set; }
        // True if Status == InProgress AND the attempt hasn't timed out yet -- the frontend uses this
        // to decide whether to offer "Resume" vs just showing it as an expired/stale row.
        public bool CanResume { get; set; }
    }
}
