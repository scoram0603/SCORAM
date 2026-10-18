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
        // All frozen at attempt-start alongside the text fields above -- see
        // StudentAnswer.QuestionImageUrlSnapshot's own comment. Null for a question with no image,
        // same as every existing consumer of Question/QuestionBankQuestion's own image fields.
        public string? QuestionImageUrl { get; set; }
        public string? OptionAImageUrl { get; set; }
        public string? OptionBImageUrl { get; set; }
        public string? OptionCImageUrl { get; set; }
        public string? OptionDImageUrl { get; set; }
        public List<ContentBlockDto> ContentBlocks { get; set; } = new();
        // Optional (Shared Stimulus): ids into TestAttemptStartResponseDto.Stimuli, in display order. Empty for every
        // question without a stimulus, and for every non-paper attempt -- old clients simply ignore it.
        public List<Guid> StimulusIds { get; set; } = new();
        public string? SelectedOption { get; set; }
        public bool IsMarkedForReview { get; set; }
        // Sectional Mock Tests only (null otherwise): the section this question belongs to.
        public int? SectionOrder { get; set; }
        public string? SectionName { get; set; }
        /// <summary>SMART PROGRESS -- running seconds already recorded for this question (null = none), so a resumed attempt keeps counting from there.</summary>
        public int? TimeSpentSeconds { get; set; }
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
        // Server's "now" at the moment this response was built. The frontend uses it to correct for a
        // student's device clock being ahead/behind, so a skewed clock can't make a fresh attempt
        // look already expired (and auto-submit it) on the very first timer tick.
        public DateTime ServerTime { get; set; }
        public string? Instructions { get; set; }
        // Optional, additive (Advanced Paper Instructions / Shared Stimulus). Empty unless a Previous Year Paper has such
        // content; each stimulus appears once here and is referenced by questions through StimulusIds.
        public List<StudentPaperInstructionDto> PaperInstructions { get; set; } = new();
        public List<StudentStimulusDto> Stimuli { get; set; } = new();
        public List<TestAttemptQuestionDto> Questions { get; set; } = new();

        // SECTION-WISE TIMING -- empty / false for every test that isn't sectional, so existing clients
        // are unaffected. When IsSectional, the client shows one section at a time with its own countdown
        // (SectionExpiresAt) and calls POST .../sections/advance to move on; DurationMinutes/ExpiresAt above
        // still describe the whole attempt (sum of the sections).
        public bool IsSectional { get; set; }
        public List<TestAttemptSectionDto> Sections { get; set; } = new();
        public int? CurrentSectionOrder { get; set; }
        public DateTime? SectionStartedAt { get; set; }
        public DateTime? SectionExpiresAt { get; set; }
    }

    public class TestAttemptSectionDto
    {
        public int Order { get; set; }
        public string Name { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
        public int QuestionCount { get; set; }
    }

    // POST /api/tests/attempts/{attemptId}/sections/advance -- body. FromSectionOrder is the section the
    // client believes it is leaving; if the server already moved past it (the client's clock and the
    // server's raced) the call just reports the current state instead of skipping another section.
    public class SectionAdvanceDto
    {
        public int FromSectionOrder { get; set; }
    }

    public class SectionStateDto
    {
        public int? CurrentSectionOrder { get; set; }
        public DateTime? SectionStartedAt { get; set; }
        public DateTime? SectionExpiresAt { get; set; }
        public DateTime ServerTime { get; set; }
        public bool IsLastSection { get; set; }
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
        // Same frozen-at-attempt-start contract as TestAttemptQuestionDto's own image/ContentBlocks
        // fields -- see StudentAnswer.QuestionImageUrlSnapshot.
        public string? QuestionImageUrl { get; set; }
        public string? OptionAImageUrl { get; set; }
        public string? OptionBImageUrl { get; set; }
        public string? OptionCImageUrl { get; set; }
        public string? OptionDImageUrl { get; set; }
        public string? ExplanationImageUrl { get; set; }
        public List<ContentBlockDto> ContentBlocks { get; set; } = new();
        public string? SelectedOption { get; set; }
        public bool IsCorrect { get; set; }
        public bool WasSkipped { get; set; }
        public string? Explanation { get; set; }
        public string? Subject { get; set; }
        public string? Topic { get; set; }
        public int? SectionOrder { get; set; }
        public string? SectionName { get; set; }

        // Optional (Shared Stimulus): ids into TestSubmitResultDto.Stimuli. Empty for questions without shared content.
        public List<Guid> StimulusIds { get; set; } = new();

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

        // RESULT RELEASE -- true while the admin is holding this Mock Test's result back (see
        // MockTest.ResultReleaseMode). While locked, the score/count fields above are zero and Questions
        // is empty -- nothing about the answers is sent -- and ResultReleaseAt says when it opens (null =
        // opens by itself at some unspecified later time).
        public bool IsResultLocked { get; set; }
        public DateTime? ResultReleaseAt { get; set; }

        // Section-wise score breakdown; empty for a test without sections.
        public List<SectionResultDto> SectionResults { get; set; } = new();
        public List<TestAnswerReviewDto> Questions { get; set; } = new();
        // Optional (Shared Stimulus): each active stimulus once; questions reference it via StimulusIds.
        public List<StudentStimulusDto> Stimuli { get; set; } = new();
    }

    public class SectionResultDto
    {
        public int Order { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TotalQuestions { get; set; }
        public int CorrectCount { get; set; }
        public int WrongCount { get; set; }
        public int SkippedCount { get; set; }
        public decimal Score { get; set; }
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
        // RESULT RELEASE -- see TestSubmitResultDto.IsResultLocked. Score/Percentage/Accuracy above are null while locked.
        public bool IsResultLocked { get; set; }
        public DateTime? ResultReleaseAt { get; set; }
    }
}
