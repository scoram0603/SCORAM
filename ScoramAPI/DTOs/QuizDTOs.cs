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

    // ============================================================================================
    // Phase 3 -- Challenge a Friend (see Models/QuizChallengeModels.cs)
    // ============================================================================================

    // POST /api/quiz-challenges. At least one of ChallengedUserIds/ChallengedGroupId is required --
    // both can be combined in one send (e.g. "these 2 friends AND my SSC CGL group"). A Group
    // (ChatRoom) expands to every current, non-banned member (see QuizChallengesController.Create)
    // -- the challenger themself and anyone already challenged for this exact attempt are silently
    // dropped from the final target list rather than erroring, so picking a big group doesn't force
    // the sender to hand-dedupe first.
    public class QuizChallengeCreateDto
    {
        [Required]
        public Guid AttemptId { get; set; }

        public List<Guid> ChallengedUserIds { get; set; } = new();

        public Guid? ChallengedGroupId { get; set; }
    }

    // Response to a successful POST /api/quiz-challenges -- one QuizChallengeSummaryDto per person
    // actually challenged, all sharing the same BatchId.
    public class QuizChallengeBatchResultDto
    {
        public Guid BatchId { get; set; }
        public List<QuizChallengeSummaryDto> Challenges { get; set; } = new();

        // How many candidates from ChallengedUserIds/the group's members were dropped (self,
        // duplicates, inactive accounts, or already challenged for this same attempt) -- surfaced so
        // the UI can say "sent to 12 of 14" instead of silently sending fewer than expected.
        public int SkippedCount { get; set; }
    }

    public class QuizChallengeSummaryDto
    {
        public Guid Id { get; set; }
        public Guid BatchId { get; set; }

        public Guid ChallengerUserId { get; set; }
        public string ChallengerName { get; set; } = string.Empty;
        public string? ChallengerPhotoUrl { get; set; }

        public Guid ChallengedUserId { get; set; }
        public string ChallengedName { get; set; } = string.Empty;
        public string? ChallengedPhotoUrl { get; set; }

        public string QuizTitle { get; set; } = string.Empty;
        public int QuestionCount { get; set; }

        public decimal ChallengerScore { get; set; }
        public decimal? ChallengedScore { get; set; } // null until the challenged student finishes

        // "Pending" | "InProgress" | "Completed" | "Declined" | "Expired" -- computed, see
        // QuizChallengesController.StatusFor.
        public string Status { get; set; } = string.Empty;

        // Only meaningful once Status == "Completed" -- "Challenger" | "Challenged" | "Tie".
        public string? Winner { get; set; }

        // So the caller's own UI knows which side of the challenge THIS student is on.
        public bool IAmChallenger { get; set; }

        public Guid? ChallengedAttemptId { get; set; }
        public Guid SourceAttemptId { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
