using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.DTOs
{
    // STUDY PARTNER DTOs. Entities are never returned directly, and none of these ever carries an
    // email, phone number, password hash or any internal-only id.

    public class SpPersonDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? PhotoUrl { get; set; }
        /// <summary>Null when the student's Profile visibility hides it from this viewer.</summary>
        public string? PrimaryExam { get; set; }
    }

    public class SpSearchResultDto : SpPersonDto
    {
        /// <summary>None | RequestSent | RequestReceived | StudyPartner | RequestRejected | Blocked | Restricted</summary>
        public string ConnectionState { get; set; } = "None";
        public Guid? RequestId { get; set; }
    }

    public class SpRequestDto
    {
        public Guid Id { get; set; }
        public SpPersonDto Person { get; set; } = new();
        public DateTime CreatedAt { get; set; }
    }

    public class SpRequestsResponseDto
    {
        public List<SpRequestDto> Incoming { get; set; } = new();
        public List<SpRequestDto> Outgoing { get; set; } = new();
    }

    public class SpSendRequestDto
    {
        [Required] public Guid ReceiverUserId { get; set; }
    }

    public class SpPartnerDto : SpPersonDto
    {
        public DateTime PartnersSince { get; set; }
        /// <summary>Null when this partner's Progress visibility hides it.</summary>
        public int? CurrentStreak { get; set; }
        public int? QuestionsLast7Days { get; set; }
    }

    public class SpProgressDto
    {
        public int QuestionsAttempted { get; set; }
        public int QuestionsCorrect { get; set; }
        public decimal AccuracyPercent { get; set; }
        public int PracticeTests { get; set; }
        public int MockTests { get; set; }
        public int PypAttempts { get; set; }
        public int Quizzes { get; set; }
        public int TestsCompleted { get; set; }
        public int CurrentStreak { get; set; }
        public int LongestStreak { get; set; }
        public int TotalXp { get; set; }
        public int Badges { get; set; }
        public int QuestionsLast7Days { get; set; }
    }

    public class SpExamRefDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class SpProfileDto : SpPersonDto
    {
        public bool IsPartner { get; set; }
        public DateTime? PartnersSince { get; set; }
        /// <summary>Null when Profile visibility hides the exam list from this viewer.</summary>
        public List<SpExamRefDto>? Exams { get; set; }
        /// <summary>Null + ProgressRestricted=true when Progress visibility hides it.</summary>
        public SpProgressDto? Progress { get; set; }
        public bool ProgressRestricted { get; set; }
    }

    public class SpActivityItemDto
    {
        public string Kind { get; set; } = string.Empty; // Practice | Mock | PreviousYearPaper | Quiz
        public string? ExamName { get; set; }
        public int QuestionsAnswered { get; set; }
        public DateTime At { get; set; }
    }

    public class SpActivityResponseDto
    {
        public bool Restricted { get; set; }
        public List<SpActivityItemDto> Items { get; set; } = new();
    }

    public class SpCompareDto
    {
        public SpExamRefDto? Exam { get; set; }
        /// <summary>Exams both students have in My Exams -- the only ones a comparison can be scoped to.</summary>
        public List<SpExamRefDto> CommonExams { get; set; } = new();
        public SpPersonDto Partner { get; set; } = new();
        public SpProgressDto Me { get; set; } = new();
        public SpProgressDto PartnerProgress { get; set; } = new();
    }

    public class SpLeaderboardEntryDto
    {
        public int Rank { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? PhotoUrl { get; set; }
        public bool IsMe { get; set; }
        public int QuestionsAttempted { get; set; }
        public decimal AccuracyPercent { get; set; }
        public int TestsCompleted { get; set; }
        public int CurrentStreak { get; set; }
        public int TotalXp { get; set; }
    }

    public class SpLeaderboardDto
    {
        public string Period { get; set; } = "week";
        public string Metric { get; set; } = "questions";
        public Guid? ExamId { get; set; }
        public List<SpLeaderboardEntryDto> Entries { get; set; } = new();
    }

    public class SpCreateChallengeDto
    {
        [Required] public Guid PartnerUserId { get; set; }
        /// <summary>Practice | Accuracy | Speed | Streak</summary>
        [Required] public string Type { get; set; } = string.Empty;
        public Guid? ExamId { get; set; }
        public int? QuestionCount { get; set; }
        public int? TargetDays { get; set; }
        public int? TimeLimitMinutes { get; set; }
        /// <summary>How long the challenge runs once accepted (1-30).</summary>
        [Range(1, 30)] public int DurationDays { get; set; } = 7;
    }

    public class SpChallengeDto
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public SpPersonDto Creator { get; set; } = new();
        public SpPersonDto Partner { get; set; } = new();
        public bool IAmCreator { get; set; }
        public SpExamRefDto? Exam { get; set; }
        public int? QuestionCount { get; set; }
        public int? TargetDays { get; set; }
        public int? TimeLimitMinutes { get; set; }
        public int DurationDays { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal? CreatorResult { get; set; }
        public decimal? PartnerResult { get; set; }
        public Guid? WinnerUserId { get; set; }
        public string? ResultSummary { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class SpPrivacyDto
    {
        /// <summary>Everyone | StudyPartnersOnly | OnlyMe</summary>
        [Required] public string ProfileVisibility { get; set; } = "Everyone";
        [Required] public string ProgressVisibility { get; set; } = "StudyPartnersOnly";
        [Required] public string ActivityVisibility { get; set; } = "StudyPartnersOnly";
        public bool ShowOnLeaderboard { get; set; } = true;
        public bool AllowStudyPartnerRequests { get; set; } = true;
    }
}
