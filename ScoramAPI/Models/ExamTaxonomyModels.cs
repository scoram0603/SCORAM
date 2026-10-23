using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.Models
{
    // ==================================================================================
    // EXAM-WISE SUBJECT & TOPIC TAXONOMY
    //
    // Before this feature, Subject and Topic were GLOBAL master data (QuestionBankSubject /
    // QuestionBankTopic) and every QuestionBankQuestion carried exactly one SubjectId + TopicId, so a
    // question had the same classification in every exam it was tagged to. The question<->exam link
    // (QuestionBankExamMapping) only carries (question, exam, year) -- no classification.
    //
    // This layer adds a per-exam taxonomy ON TOP of those tables, without touching them:
    //
    //   ExamSubject / ExamTopic          the subjects/topics of ONE exam. Each has its own stable Id and
    //                                    its OWN Name (copied from the global record when it was
    //                                    initialised, then independent), so renaming it affects only
    //                                    that exam. BaseSubjectId / BaseTopicId are an optional LINEAGE
    //                                    link to the global record it was created from (never required,
    //                                    never used for naming).
    //   QuestionBankExamClassification   "in THIS exam, THIS question is classified under THIS exam
    //                                    subject/topic". One row per (question, exam). The question
    //                                    content is never copied; the same question can have a
    //                                    different classification in every exam.
    //
    // Resolution rule used everywhere (see ExamTaxonomyService):
    //   1. a classification row exists  -> use it (ExamSubjectId/ExamTopicId may be NULL = explicitly
    //                                      "unassigned" in this exam);
    //   2. no row                       -> legacy fallback: the exam subject/topic whose Base* link equals
    //                                      the question's global SubjectId/TopicId, if any.
    // so every pre-existing question keeps working with no data change at all until an admin chooses to
    // "initialise" an exam (a previewed, confirmed, idempotent action).
    //
    // "Delete" of an ExamSubject/ExamTopic never touches questions: it is either an archive
    // (IsActive=false) or an explicit unassign-then-remove that the admin confirms with counts.
    // ==================================================================================

    public class ExamSubject
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ExamId { get; set; }
        public Exam? Exam { get; set; }

        // Lineage only (nullable): the global subject this was created from. NOT used for the name.
        public Guid? BaseSubjectId { get; set; }
        public QuestionBankSubject? BaseSubject { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        // false = archived for this exam (hidden from student filters/pickers, mappings kept, restorable).
        public bool IsActive { get; set; } = true;

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public ICollection<ExamTopic> Topics { get; set; } = new List<ExamTopic>();
    }

    public class ExamTopic
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ExamSubjectId { get; set; }
        public ExamSubject? ExamSubject { get; set; }

        // Lineage only (nullable): the global topic this was created from.
        public Guid? BaseTopicId { get; set; }
        public QuestionBankTopic? BaseTopic { get; set; }

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }

    public class QuestionBankExamClassification
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid QuestionBankQuestionId { get; set; }
        public QuestionBankQuestion? QuestionBankQuestion { get; set; }

        public Guid ExamId { get; set; }
        public Exam? Exam { get; set; }

        // NULL subject = explicitly unassigned in this exam (the row exists so the legacy fallback does
        // not silently re-attach the question). A topic is only ever set together with its subject.
        public Guid? ExamSubjectId { get; set; }
        public ExamSubject? ExamSubject { get; set; }

        public Guid? ExamTopicId { get; set; }
        public ExamTopic? ExamTopic { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedByAdminId { get; set; }
    }
}
