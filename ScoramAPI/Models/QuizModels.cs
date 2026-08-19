using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    // Phase 2 of the "Quizzes" feature (see TestKind.Quiz's own comment for Phase 1, Weak Topics
    // Quiz) -- an admin-curated, short quiz with its own live window, e.g. a daily Current Affairs
    // quiz. Deliberately modeled after MockTest (Title/Duration/NegativeMarkingRatio/Status/
    // scheduling window/CreatedByAdmin) rather than invented fresh, so it feels native to admins
    // already familiar with Mock Test management instead of a one-off pattern. The one deliberate
    // difference: no ExamName/TestType (a themed quiz doesn't represent one specific competitive
    // exam the way a Mock Test does) and no legacy-Question dual-FK on its question link table --
    // a Quiz always draws from the Question Bank, same restriction Practice Tests already have.
    public class Quiz
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(150)]
        public string Title { get; set; } = string.Empty;

        // Free-text theme label shown to students (e.g. "Current Affairs", "Reasoning Boost") --
        // doesn't drive any filtering logic itself; Question Bank Subject/Topic already do that on
        // the question side when the admin picks questions below.
        [MaxLength(100)]
        public string? Topic { get; set; }

        public int DurationMinutes { get; set; } = 10;

        public decimal NegativeMarkingRatio { get; set; } = 0m;

        // The window during which students can see/start this quiz -- same idea as
        // MockTest.ScheduledAt/EndAt, named differently here since "Scheduled" implies a single
        // sitting whereas a quiz is meant to just be "live" for however long an admin sets (a whole
        // day, for a genuinely daily quiz). Both nullable: no AvailableFrom = live immediately once
        // Published; no AvailableTo = stays live indefinitely.
        public DateTime? AvailableFrom { get; set; }
        public DateTime? AvailableTo { get; set; }

        // Null = unlimited attempts. Defaults to 1 (unlike MockTest's default-unlimited) since the
        // whole point of a Daily Quiz is a once-a-day check-in, not a paper to be redone -- still
        // admin-configurable per quiz for whichever ones should allow retries.
        public int? MaxAttempts { get; set; } = 1;

        public TestPublishStatus Status { get; set; } = TestPublishStatus.Draft;

        public Guid CreatedByAdminId { get; set; }
        public Admin? CreatedByAdmin { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<QuizQuestion> QuizQuestions { get; set; } = new List<QuizQuestion>();
        public ICollection<StudentTestResult> Results { get; set; } = new List<StudentTestResult>();
    }

    public class QuizQuestion
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid QuizId { get; set; }
        public Quiz? Quiz { get; set; }

        // Question Bank only -- see Quiz's own comment on why there's no legacy-Question dual-FK
        // here the way MockTestQuestion/PaperQuestionBankLink have.
        public Guid QuestionBankQuestionId { get; set; }
        public QuestionBankQuestion? QuestionBankQuestion { get; set; }

        public int QuestionOrder { get; set; }
    }
}
