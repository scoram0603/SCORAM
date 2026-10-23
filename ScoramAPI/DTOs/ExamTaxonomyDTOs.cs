using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.DTOs
{
    // DTOs for Admin > Exam-wise Subject & Topic Management (api/admin/exam-taxonomy/*).
    // All names/ids here are EXAM-SPECIFIC (ExamSubject / ExamTopic), never the global QuestionBankSubject ids.

    public class ExamTaxonomyExamSummaryDto
    {
        public Guid Id { get; set; }
        public string? BusinessId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int QuestionCount { get; set; }      // distinct Question Bank questions tagged to this exam
        public int SubjectCount { get; set; }       // exam subjects (active + archived)
        public bool Initialized { get; set; }       // true once the exam has at least one ExamSubject row
    }

    public class ExamTaxonomyOverviewDto
    {
        public ExamTaxonomyExamSummaryDto Exam { get; set; } = new();
        public bool Initialized { get; set; }
        public int TotalQuestions { get; set; }
        public int UnassignedQuestions { get; set; }     // mapped to the exam but under no exam subject
        public List<ExamSubjectItemDto> Subjects { get; set; } = new();
    }

    public class ExamSubjectItemDto
    {
        public Guid Id { get; set; }
        public Guid ExamId { get; set; }
        public Guid? BaseSubjectId { get; set; }
        public string? BaseSubjectName { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int TopicCount { get; set; }
        public int ActiveTopicCount { get; set; }
        public int QuestionCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string Version { get; set; } = string.Empty;
    }

    public class ExamTopicItemDto
    {
        public Guid Id { get; set; }
        public Guid ExamSubjectId { get; set; }
        public Guid? BaseTopicId { get; set; }
        public string? BaseTopicName { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int QuestionCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string Version { get; set; } = string.Empty;
    }

    public class ExamTopicListDto
    {
        public ExamSubjectItemDto Subject { get; set; } = new();
        public List<ExamTopicItemDto> Topics { get; set; } = new();
    }

    // What an exam subject/topic is used by. Questions/Papers/MockTests/PracticeTests/Quizzes are the
    // content that follows the classified questions (they are never modified by taxonomy changes);
    // StudentAnswers is recorded history, shown so the admin can see it is preserved.
    public class ExamTaxonomyUsageDto
    {
        public int Questions { get; set; }
        public int Topics { get; set; }
        public int ActiveTopics { get; set; }
        public int Papers { get; set; }
        public int MockTests { get; set; }
        public int PracticeTests { get; set; }
        public int Quizzes { get; set; }
        public int StudentAnswers { get; set; }
    }

    public class ExamTaxonomyDeletePreviewDto
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = "subject"; // subject | topic
        public string Name { get; set; } = string.Empty;
        public ExamTaxonomyUsageDto Usage { get; set; } = new();
        public bool CanDelete { get; set; }            // plain delete: nothing depends on it
        public bool CanArchive { get; set; }           // soft-hide, always safe (when still active)
        public bool CanUnassign { get; set; }          // unassign the questions, then remove
        public List<string> Blockers { get; set; } = new();
        public string Message { get; set; } = string.Empty;
    }

    public class ExamSubjectCreateDto
    {
        [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
        // Optional lineage link to an existing GLOBAL subject (does not affect the name or other exams).
        public Guid? BaseSubjectId { get; set; }
    }

    public class ExamTopicCreateDto
    {
        [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
        public Guid? BaseTopicId { get; set; }
    }

    public class ExamTaxonomyRenameDto
    {
        [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
        // Stale-edit token from the list/detail response; omit to skip the check.
        public string? Version { get; set; }
    }

    public class ExamTaxonomySetActiveDto
    {
        public bool IsActive { get; set; }
        public string? Version { get; set; }
    }

    public class ExamTaxonomyInitPreviewDto
    {
        public Guid ExamId { get; set; }
        public string ExamName { get; set; } = string.Empty;
        public bool AlreadyInitialized { get; set; }
        public int QuestionsInExam { get; set; }
        public int QuestionsToClassify { get; set; }    // mapped to the exam with no classification row yet
        public int SubjectsToCreate { get; set; }
        public int TopicsToCreate { get; set; }
        public List<ExamTaxonomyInitPreviewSubjectDto> Subjects { get; set; } = new();
        // Names that would collide with an existing ACTIVE exam subject/topic. Initialisation refuses to
        // run until an admin renames one side -- it never renames or merges anything on its own.
        public List<string> Conflicts { get; set; } = new();
        public bool CanRun { get; set; }
    }

    public class ExamTaxonomyInitPreviewSubjectDto
    {
        public string Name { get; set; } = string.Empty;
        public bool Exists { get; set; }
        public int Questions { get; set; }
        public List<string> Topics { get; set; } = new();
    }

    public class ExamTaxonomyInitRequestDto
    {
        public bool Confirm { get; set; }
        // From the preview; if the live number differs nothing runs (409 IMPACT_CHANGED).
        public int? ExpectedQuestionsToClassify { get; set; }
    }

    public class ExamTaxonomyQuestionQueryDto
    {
        public Guid? ExamSubjectId { get; set; }
        public Guid? ExamTopicId { get; set; }
        public bool Unassigned { get; set; }
        public string? Search { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    public class ExamTaxonomyQuestionDto
    {
        public Guid Id { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string GlobalSubject { get; set; } = string.Empty;   // the question's own global classification (unchanged)
        public string GlobalTopic { get; set; } = string.Empty;
        public Guid? ExamSubjectId { get; set; }
        public string? ExamSubjectName { get; set; }
        public Guid? ExamTopicId { get; set; }
        public string? ExamTopicName { get; set; }
        public bool Explicit { get; set; }       // false = legacy fallback (no classification row)
        public List<int> Years { get; set; } = new();
    }

    public class ExamTaxonomyQuestionListDto
    {
        public List<ExamTaxonomyQuestionDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class ExamTaxonomyMapRequestDto
    {
        public List<Guid> QuestionIds { get; set; } = new();
        public Guid ExamSubjectId { get; set; }
        public Guid? ExamTopicId { get; set; }
    }

    public class ExamTaxonomyUnmapRequestDto
    {
        public List<Guid> QuestionIds { get; set; } = new();
    }

    public class QuestionExamClassificationDto
    {
        public Guid ExamId { get; set; }
        public string ExamName { get; set; } = string.Empty;
        public List<int> Years { get; set; } = new();
        public Guid? ExamSubjectId { get; set; }
        public string? ExamSubjectName { get; set; }
        public Guid? ExamTopicId { get; set; }
        public string? ExamTopicName { get; set; }
        public bool Explicit { get; set; }
    }

    public class ExamTaxonomyResultDto
    {
        public string Message { get; set; } = string.Empty;
        public int Affected { get; set; }
        public ExamSubjectItemDto? Subject { get; set; }
        public ExamTopicItemDto? Topic { get; set; }
    }

    // ---- Student-facing additions (QuestionBankController, optional examId) ----
    public class ExamSubjectFilterDto
    {
        public Guid Id { get; set; }          // ExamSubject id -- pass as examSubjectIds to /search
        public string Name { get; set; } = string.Empty;
        public int QuestionCount { get; set; }
        public bool IsExamSpecific { get; set; } = true;
    }

    public class ExamTopicFilterDto
    {
        public Guid Id { get; set; }          // ExamTopic id -- pass as examTopicIds to /search
        public Guid SubjectId { get; set; }   // the ExamSubject id
        public string SubjectName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int QuestionCount { get; set; }
        public bool IsExamSpecific { get; set; } = true;
    }
}
