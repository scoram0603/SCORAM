using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    public class MockTest
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(150)]
        public string Title { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string ExamName { get; set; } = string.Empty;

        public MockTestType TestType { get; set; } = MockTestType.FullMockTest;

        public int DurationMinutes { get; set; }

        public decimal NegativeMarkingRatio { get; set; } = 0.25m;

        public bool IsRandomOrder { get; set; } = false;

        public bool IsShuffleOptions { get; set; } = false;

        public DateTime? ScheduledAt { get; set; }

        // SCORAM_TESTS additions below -- all additive/nullable-or-defaulted so existing rows and
        // existing API callers that don't know about these fields keep working unchanged.

        // Paired with ScheduledAt (treated as the start) to give a real availability window --
        // previously a MockTest had no defined end, so "Upcoming/Live/Completed" status (which the
        // spec calls for) had no way to be computed.
        public DateTime? EndAt { get; set; }

        // Draft: admin-only, not visible to students. Published: students can see/start it (subject
        // to the ScheduledAt/EndAt window). Archived: hidden from new attempts but its historical
        // attempts/results remain intact -- deleting a MockTest outright isn't safe once anyone has
        // attempted it.
        public TestPublishStatus Status { get; set; } = TestPublishStatus.Draft;

        // Null = unlimited attempts (matches the original behavior, since nothing enforced a limit
        // before this field existed).
        public int? MaxAttempts { get; set; }

        public string? Instructions { get; set; }

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<MockTestQuestion> MockTestQuestions { get; set; } = new List<MockTestQuestion>();
        public ICollection<StudentTestResult> Results { get; set; } = new List<StudentTestResult>();
    }

    public class MockTestQuestion
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid MockTestId { get; set; }
        public MockTest? MockTest { get; set; }

        // Nullable so a paper can mix legacy PYQ questions and Question Bank questions -- same
        // dual-FK reuse pattern as QuestionSolution/QuestionReport/QuestionComment/QuestionVote.
        // Exactly one of QuestionId/QuestionBankQuestionId is set.
        public Guid? QuestionId { get; set; }
        public Question? Question { get; set; }

        public Guid? QuestionBankQuestionId { get; set; }
        public QuestionBankQuestion? QuestionBankQuestion { get; set; }

        public int QuestionOrder { get; set; }
    }

    public class StudentTestResult
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        // SCORAM_TESTS: Practice and Mock attempts now share this one table. MockTestId is nullable
        // because a Practice attempt (ad-hoc or from a PracticeTestTemplate) isn't tied to a MockTest
        // row at all.
        public TestKind TestKind { get; set; } = TestKind.Mock;

        public Guid? MockTestId { get; set; }
        public MockTest? MockTest { get; set; }

        // Set only for a Practice attempt generated FROM a named admin template (section B of the
        // Practice Tests spec). Null for a purely ad-hoc, student-configured Practice attempt
        // (section A) -- the PracticeFilters* columns below capture what was actually used either way.
        public Guid? PracticeTestTemplateId { get; set; }
        public PracticeTestTemplate? PracticeTestTemplate { get; set; }

        // Set only for TestKind.PreviousYearPaper -- which real previous-year Paper this attempt is
        // for. Null for Mock/Practice. See Models/Paper.cs and StudentPapersController.Start.
        public Guid? PaperId { get; set; }
        public Paper? Paper { get; set; }

        // Set only for a TestKind.Quiz attempt generated FROM an admin-curated Quiz (Phase 2, "Daily
        // Quiz" -- see Models/QuizModels.cs). Null for a Weak Topics Quiz attempt (Phase 1), which
        // has no such row to point at -- QuizDurationMinutes above captures its settings the same way
        // PracticeDurationMinutes does for an ad-hoc Practice attempt.
        public Guid? QuizId { get; set; }
        public Quiz? Quiz { get; set; }

        // Snapshot of the filters/settings actually used to generate a Practice attempt -- kept on
        // the attempt itself (not just looked up via PracticeTestTemplateId) so it stays accurate
        // even for template-less ad-hoc attempts, and even if a template is edited/deleted later.
        // All null for TestKind.Mock (MockTest already carries its own settings).
        public Guid? PracticeSubjectId { get; set; }
        public Guid? PracticeTopicId { get; set; }
        public Guid? PracticeExamId { get; set; }
        public int? PracticeYearFrom { get; set; }
        public int? PracticeYearTo { get; set; }
        public DifficultyLevel? PracticeDifficulty { get; set; }
        // Only meaningful for an ad-hoc attempt (no PracticeTestTemplateId) -- a templated attempt
        // uses the template's own DurationMinutes instead, so this stays null for those.
        public int? PracticeDurationMinutes { get; set; }

        // TestKind.Quiz only. No Quiz "template" entity exists (Phase 1 is Weak Topics Quiz, which
        // is recomputed fresh every time -- see TestKind.Quiz's own comment) so, like an ad-hoc
        // Practice attempt above, the duration actually used has to be snapshotted onto the attempt
        // itself rather than looked up from somewhere else.
        public int? QuizDurationMinutes { get; set; }

        // Snapshotted at attempt-start (from the MockTest for Mock, or the generation request /
        // template for Practice) so a later admin edit to negative marking never changes the scoring
        // of an attempt already in progress or completed.
        public decimal NegativeMarkingRatio { get; set; } = 0.25m;

        public Guid UserId { get; set; }
        public User? User { get; set; }

        // SCORAM_TESTS: attempts now exist from the moment a test is STARTED, not just at
        // submission -- this is what makes auto-save and resuming a dropped attempt possible.
        public TestAttemptStatus Status { get; set; } = TestAttemptStatus.InProgress;

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public decimal Score { get; set; }

        public int CorrectCount { get; set; }
        public int WrongCount { get; set; }
        public int SkippedCount { get; set; }

        public int TimeTakenSeconds { get; set; }

        public int? Rank { get; set; }

        // Kept as the existing column name for backward compatibility (existing queries/DTOs already
        // read AttemptedAt) -- now specifically means "submitted at", set only once Status leaves
        // InProgress. Nullable would be more precise but every pre-existing row already has a real
        // value here (they were only ever created at submission time before), so keeping it non-null
        // and simply "not yet meaningful" while InProgress avoids a data migration for old rows.
        public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;

        public ICollection<StudentAnswer> Answers { get; set; } = new List<StudentAnswer>();
    }
}
