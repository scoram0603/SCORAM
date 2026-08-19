using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.DTOs
{
    // GET /api/quizzes/weak-topics/preview -- one row per weak subject, for a "Your weak areas:
    // Reasoning (42%)" style preview before the student commits to starting.
    public class WeakSubjectDto
    {
        public string Subject { get; set; } = string.Empty;

        // 0-100, rounded -- how often the student got a QUESTION in this subject right, across their
        // own graded history (see TestAttemptService.GetWeakSubjectsAsync).
        public double Accuracy { get; set; }

        public int AnswersConsidered { get; set; }
    }

    // POST /api/quizzes/weak-topics/generate. Everything optional -- Phase 1 is deliberately
    // zero-config ("we already know what you're weak at"); QuestionCount is the only knob, and even
    // that just picks a preset rather than a free-form number, to keep the whole flow one tap.
    public class QuizGenerateDto
    {
        [Range(5, 20)]
        public int QuestionCount { get; set; } = 8;
    }

    // ============================================================================================
    // Phase 2 -- admin-curated Daily Quiz (see Models/QuizModels.cs)
    // ============================================================================================

    public class QuizCreateDto
    {
        [Required, MaxLength(150)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Topic { get; set; }

        [Range(1, 180)]
        public int DurationMinutes { get; set; } = 10;

        [Range(0, 2)]
        public decimal NegativeMarkingRatio { get; set; } = 0m;

        public DateTime? AvailableFrom { get; set; }
        public DateTime? AvailableTo { get; set; }

        // Null = unlimited. Defaults to 1 -- see Quiz.MaxAttempts.
        public int? MaxAttempts { get; set; } = 1;

        public string Status { get; set; } = "Draft"; // Draft | Published | Archived
    }

    public class QuizUpdateDto : QuizCreateDto
    {
    }

    public class QuizSummaryDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Topic { get; set; }
        public int DurationMinutes { get; set; }
        public decimal NegativeMarkingRatio { get; set; }
        public int QuestionCount { get; set; }
        public DateTime? AvailableFrom { get; set; }
        public DateTime? AvailableTo { get; set; }
        public string Status { get; set; } = string.Empty;
        // "Upcoming" | "Live" | "Completed" | "Draft" | "Archived" -- same computed-availability
        // idea as MockTestSummaryDto.AvailabilityStatus (see QuizzesController.ComputeAvailability).
        public string AvailabilityStatus { get; set; } = string.Empty;
        public int? MaxAttempts { get; set; }
        // How many attempts THIS student has already used, if authenticated -- null for anonymous
        // or when this DTO is used in an admin-only context.
        public int? MyAttemptCount { get; set; }
    }

    // One row of a Quiz's question list, WITH the answer key -- admin only.
    public class QuizQuestionAdminDto
    {
        public Guid QuizQuestionId { get; set; }
        public int QuestionOrder { get; set; }
        public Guid QuestionBankQuestionId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public string CorrectOption { get; set; } = string.Empty;
    }

    public class QuizDetailDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Topic { get; set; }
        public int DurationMinutes { get; set; }
        public decimal NegativeMarkingRatio { get; set; }
        public DateTime? AvailableFrom { get; set; }
        public DateTime? AvailableTo { get; set; }
        public int? MaxAttempts { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<QuizQuestionAdminDto> Questions { get; set; } = new();
    }

    // POST /api/admin/quizzes/{id}/questions -- add several Question Bank questions at once. No
    // per-question order needed from the caller (auto-assigned sequentially after whatever's
    // already on the quiz) -- unlike a Previous Year Paper, a Quiz has no "real original position"
    // to be exact about, so there's nothing an approximate-numbering flag would need to protect here.
    public class QuizQuestionsAddDto
    {
        [Required, MinLength(1)]
        public List<Guid> QuestionBankQuestionIds { get; set; } = new();
    }
}
