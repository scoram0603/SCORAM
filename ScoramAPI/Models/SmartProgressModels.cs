using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Enums
{
    /// <summary>SMART PROGRESS -- what kind of daily study task this is. Progress for every type is
    /// DERIVED from real activity (revision answers, submitted assessments); opening a page never
    /// moves it.</summary>
    public enum StudyTaskType
    {
        /// <summary>Answer N due questions from the Mistake Book (progress = revision answers today).</summary>
        ReviseMistakes,
        /// <summary>Answer N questions of one subject/topic inside any submitted assessment today.</summary>
        PracticeTopic,
        /// <summary>Submit at least one timed assessment (Practice / Mock / Quiz / PYP) today.</summary>
        TimedSession
    }

    public enum StudyTaskStatus
    {
        Pending,
        Completed,
        Dismissed
    }
}

namespace ScoramAPI.Models
{
    /// <summary>
    /// SMART PROGRESS -- one answer given in the Mistake Book's revision flow.
    ///
    /// Deliberately NOT a StudentTestResult/StudentAnswer: a revision attempt must never overwrite or
    /// inflate an original assessment's score, rank, XP or history. Question content is not copied here
    /// -- it is looked up from the student's own StudentAnswer snapshot via the question reference.
    /// There is no foreign key to the question tables on purpose: questions may be deleted/archived
    /// later and the history of what the student answered must survive that.
    /// </summary>
    public class RevisionAttempt
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        // Exactly one of these two is set -- same dual-reference pattern as StudentAnswer.
        public Guid? QuestionBankQuestionId { get; set; }
        public Guid? QuestionId { get; set; }

        public OptionLetter SelectedOption { get; set; }
        public bool IsCorrect { get; set; }

        /// <summary>Seconds on this question as reported by the client (clamped server-side). Null = not reported.</summary>
        public int? TimeSpentSeconds { get; set; }

        // Frozen copy of the taxonomy names (from the student's answer snapshot) so topic-wise revision
        // outcomes can be grouped without joining back to anything that may have been renamed.
        [MaxLength(150)] public string? SubjectSnapshot { get; set; }
        [MaxLength(200)] public string? TopicSnapshot { get; set; }

        public DateTime AnsweredAt { get; set; } = DateTime.UtcNow;

        /// <summary>Idempotency key supplied by the client so a retried POST after a timeout is not
        /// recorded twice. Unique per user when present.</summary>
        public Guid? ClientRequestId { get; set; }
    }

    /// <summary>
    /// SMART PROGRESS -- the student's revision STATE for one question (a row exists only once the
    /// student has revised it or dismissed it; the Mistake Book list itself is derived from
    /// StudentAnswers so nothing is duplicated). One row per (user, question).
    /// </summary>
    public class MistakeRevisionItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public Guid? QuestionBankQuestionId { get; set; }
        public Guid? QuestionId { get; set; }

        /// <summary>True = the student hid this mistake from the Mistake Book. Reversible.</summary>
        public bool IsDismissed { get; set; }

        public int RevisionAttemptCount { get; set; }
        public int RevisionCorrectCount { get; set; }

        /// <summary>Correct revision answers in a row, reset to 0 by a wrong one. Reaching
        /// Analytics:CorrectedAfterConsecutiveCorrect marks the mistake "Corrected".</summary>
        public int ConsecutiveCorrect { get; set; }

        public DateTime? LastRevisedAt { get; set; }

        /// <summary>When this question is next due for revision (null = due now / never revised).</summary>
        public DateTime? NextDueAt { get; set; }

        public DateTime? CorrectedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// SMART PROGRESS -- one task on a student's daily plan. PlanDate is the IST calendar day (the same
    /// day boundary the streak uses). TaskKey is deterministic ("revise", "timed",
    /// "practice:{subject}|{topic}") so generating a day's plan twice is idempotent.
    /// </summary>
    public class StudyPlanTask
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public DateOnly PlanDate { get; set; }

        [Required, MaxLength(300)]
        public string TaskKey { get; set; } = string.Empty;

        public StudyTaskType TaskType { get; set; }

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        /// <summary>Plain-language reason, always derived from the student's recorded performance.</summary>
        [MaxLength(500)]
        public string? Reason { get; set; }

        public int TargetCount { get; set; }

        /// <summary>Last progress value persisted (the API recomputes live progress on every read; this
        /// is only a stored copy, kept so history stays understandable after the underlying data moves on).</summary>
        public int ProgressCount { get; set; }

        public StudyTaskStatus Status { get; set; } = StudyTaskStatus.Pending;

        [MaxLength(150)] public string? SubjectName { get; set; }
        [MaxLength(200)] public string? TopicName { get; set; }

        /// <summary>Lower = more important.</summary>
        public int Priority { get; set; }

        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public DateTime? DismissedAt { get; set; }
    }
}
