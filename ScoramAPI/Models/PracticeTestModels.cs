using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    // ==================================================================================
    // SCORAM_TESTS — Practice Tests support two flows from the same underlying pieces:
    //   (A) Ad-hoc: a student picks Subject/Topic/Exam/Year/Difficulty/Count/Duration themselves and
    //       generates a one-off attempt. No PracticeTestTemplate row involved at all.
    //   (B) Curated: an admin saves a named, reusable set of those same filters (optionally with an
    //       explicit fixed question list override) as a PracticeTestTemplate, which then shows up in
    //       a student-browsable list ("Weekly History Practice", etc).
    // Both ultimately produce the exact same kind of attempt (StudentTestResult with
    // TestKind.Practice) via the same generation logic -- see Services/TestAttemptService.cs.
    // ==================================================================================

    public class PracticeTestTemplate
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(150)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        // Filter criteria -- all optional/nullable, same as the ad-hoc generation request
        // (DTOs/PracticeTestDTOs.cs's PracticeTestGenerateDto). A null filter means "any" for that
        // dimension when the pool is assembled.
        public Guid? SubjectId { get; set; }
        public QuestionBankSubject? Subject { get; set; }

        public Guid? TopicId { get; set; }
        public QuestionBankTopic? Topic { get; set; }

        public Guid? ExamId { get; set; }
        public Exam? Exam { get; set; }

        public int? YearFrom { get; set; }
        public int? YearTo { get; set; }

        public DifficultyLevel? Difficulty { get; set; }

        public int QuestionCount { get; set; } = 20;

        public int DurationMinutes { get; set; } = 20;

        public decimal NegativeMarkingRatio { get; set; } = 0m;

        public bool IsRandomOrder { get; set; } = true;

        // If this template has any PracticeTestTemplateQuestion rows, those exact questions are used
        // every time (a genuinely fixed paper, same idea as MockTestQuestion) instead of generating a
        // fresh pool from the filters above on each attempt.
        public ICollection<PracticeTestTemplateQuestion> Questions { get; set; } = new List<PracticeTestTemplateQuestion>();

        public TestPublishStatus Status { get; set; } = TestPublishStatus.Draft;

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }

    // Present only for a "Curated" template (see PracticeTestTemplate.Questions above) -- a
    // FilterBased template simply has zero rows here.
    public class PracticeTestTemplateQuestion
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid PracticeTestTemplateId { get; set; }
        public PracticeTestTemplate? PracticeTestTemplate { get; set; }

        public Guid? QuestionId { get; set; }
        public Question? Question { get; set; }

        public Guid? QuestionBankQuestionId { get; set; }
        public QuestionBankQuestion? QuestionBankQuestion { get; set; }

        public int QuestionOrder { get; set; }
    }
}
