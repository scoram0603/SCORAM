using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.DTOs
{
    // ==================================================================================
    // SMART PROGRESS -- response/request shapes shared by the React website and the Flutter app.
    // Field-naming rules (see SMART_PROGRESS.md):
    //   *AccuracyPercent  = correct / answered, 0-100, NULL when nothing was answered.
    //   *ScorePercent     = score / total questions, can be negative with negative marking.
    //   *ChangePoints     = a difference of two percentages, in percentage POINTS.
    //   *RelativeChangePercent (not used by default) would be a different thing -- never mixed.
    // Enum-like values are plain strings (same convention as the rest of the API).
    // ==================================================================================

    // ---------------------------------------------------------------- shared building blocks

    /// <summary>Answered/correct counts for one slice of history, with the accuracy derived from them.</summary>
    public class WindowStatsDto
    {
        public int Attempted { get; set; }
        public int Correct { get; set; }
        public decimal? AccuracyPercent { get; set; }
        /// <summary>Human label for the slice, e.g. "Last 30 days" or "Previous 30 days".</summary>
        public string? Label { get; set; }
    }

    /// <summary>A comparison of two windows. Status: Improved | Declined | Stable | NotEnoughData.</summary>
    public class ChangeDto
    {
        public string Status { get; set; } = "NotEnoughData";
        public WindowStatsDto Current { get; set; } = new();
        public WindowStatsDto Previous { get; set; } = new();
        /// <summary>Current accuracy minus previous accuracy, in percentage POINTS. Null when NotEnoughData.</summary>
        public decimal? AccuracyChangePoints { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>One explainable next step. ActionType: Practice | Revise | Retest | Review | None.</summary>
    public class RecommendationDto
    {
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        /// <summary>Always the recorded numbers behind the advice -- never generic filler.</summary>
        public string Reason { get; set; } = string.Empty;
        public string ActionType { get; set; } = "None";
        public string? Subject { get; set; }
        public string? Topic { get; set; }
    }

    public class RevisionOutcomeDto
    {
        public int Attempted { get; set; }
        public int Correct { get; set; }
        public decimal? AccuracyPercent { get; set; }
    }

    /// <summary>Which QuestionBank subject/topic ids a topic maps onto, so a client can open the
    /// existing Practice Test generator pre-filtered. Null when the name does not map to exactly one
    /// active Question Bank subject/topic (never guessed).</summary>
    public class PracticeTargetDto
    {
        public Guid? SubjectId { get; set; }
        public Guid? TopicId { get; set; }
        public Guid? ExamId { get; set; }
    }

    // ---------------------------------------------------------------- subject / topic insights

    /// <summary>Performance for one topic across the student's completed assessments (filtered).</summary>
    public class TopicInsightDto
    {
        public string Subject { get; set; } = string.Empty;
        /// <summary>"Unclassified" when the questions carry no topic.</summary>
        public string Topic { get; set; } = string.Empty;
        public bool IsUnclassified { get; set; }
        public int Attempted { get; set; }
        public int Correct { get; set; }
        public int Incorrect { get; set; }
        public int Skipped { get; set; }
        public decimal? AccuracyPercent { get; set; }
        /// <summary>Average seconds per answered question over the questions that carry a timing; null if none do.</summary>
        public decimal? AvgSecondsPerAnswered { get; set; }
        public int TimedQuestions { get; set; }
        /// <summary>InsufficientData | NeedsAttention | PracticeRecommended | Improving | PerformingWell | NeedsRetesting</summary>
        public string Status { get; set; } = "InsufficientData";
        /// <summary>Insufficient | Preliminary | Higher</summary>
        public string Confidence { get; set; } = "Insufficient";
        public DateTime? LastAttemptedAt { get; set; }
        /// <summary>Recent window vs the window before it (length = Analytics:ComparisonWindowDays).</summary>
        public ChangeDto Trend { get; set; } = new();
        public int OpenMistakes { get; set; }
        public int CorrectedMistakes { get; set; }
        /// <summary>Mistake Book revision answers for this topic -- kept apart from the original accuracy. Null when an exam/kind filter makes them unattributable.</summary>
        public RevisionOutcomeDto? Revision { get; set; }
        public List<RecommendationDto> Recommendations { get; set; } = new();
        public PracticeTargetDto? PracticeTarget { get; set; }
    }

    public class SubjectInsightDto
    {
        public string Subject { get; set; } = string.Empty;
        public bool IsUnclassified { get; set; }
        public int Attempted { get; set; }
        public int Correct { get; set; }
        public int Incorrect { get; set; }
        public int Skipped { get; set; }
        public decimal? AccuracyPercent { get; set; }
        public decimal? AvgSecondsPerAnswered { get; set; }
        public string Confidence { get; set; } = "Insufficient";
        public ChangeDto Trend { get; set; } = new();
        public int WeakTopicCount { get; set; }
        public int TopicCount { get; set; }
        /// <summary>Filled by /api/progress/subjects; empty in the overview.</summary>
        public List<TopicInsightDto> Topics { get; set; } = new();
    }

    public class ProgressFiltersDto
    {
        public Guid? ExamId { get; set; }
        public string? Kind { get; set; }
        public int? Days { get; set; }
        public bool MyExamsOnly { get; set; }
    }

    public class DataQualityDto
    {
        public int TotalAnswers { get; set; }
        public int AnswersWithoutSubject { get; set; }
        public int AnswersWithoutTopic { get; set; }
        /// <summary>Plain-language limitations for the screen to show (empty when everything is classified).</summary>
        public List<string> Notes { get; set; } = new();
    }

    // ---------------------------------------------------------------- overview

    public class ActivityTotalsDto
    {
        public int CompletedAssessments { get; set; }
        public int QuestionsAnswered { get; set; }
        public int Correct { get; set; }
        public int Incorrect { get; set; }
        public int Skipped { get; set; }
        public decimal? AccuracyPercent { get; set; }
        public decimal? AvgSecondsPerAnswered { get; set; }
        public List<KindCountDto> ByKind { get; set; } = new();
    }

    public class KindCountDto
    {
        public string TestKind { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal? AvgAccuracyPercent { get; set; }
    }

    public class RecentAssessmentDto
    {
        public Guid AttemptId { get; set; }
        public string TestKind { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
        public decimal Score { get; set; }
        public int TotalQuestions { get; set; }
        public decimal? AccuracyPercent { get; set; }
        public decimal? ScorePercent { get; set; }
        public int TimeTakenSeconds { get; set; }
    }

    public class TrendPointDto
    {
        public Guid AttemptId { get; set; }
        public DateTime Date { get; set; }
        public string TestKind { get; set; } = string.Empty;
        public decimal? AccuracyPercent { get; set; }
        public decimal? ScorePercent { get; set; }
        public int Attempted { get; set; }
    }

    public class RevisionSummaryDto
    {
        /// <summary>Mistakes still to be corrected (status New or Learning).</summary>
        public int Open { get; set; }
        public int New { get; set; }
        public int Learning { get; set; }
        public int Corrected { get; set; }
        public int Dismissed { get; set; }
        /// <summary>Open mistakes that have been wrong more than once.</summary>
        public int Repeated { get; set; }
        public int DueNow { get; set; }
        public int RevisionAnswersLast7Days { get; set; }
    }

    public class PlanSummaryDto
    {
        public string Date { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Completed { get; set; }
        public int Dismissed { get; set; }
        public int Pending { get; set; }
    }

    public class ProgressOverviewDto
    {
        public DateTime GeneratedAt { get; set; }
        public ProgressFiltersDto Filters { get; set; } = new();
        public ActivityTotalsDto Totals { get; set; } = new();
        public ChangeDto Last7Days { get; set; } = new();
        public ChangeDto Last30Days { get; set; } = new();
        public List<RecentAssessmentDto> RecentActivity { get; set; } = new();
        /// <summary>All subjects (without their topics), weakest first among those with enough data.</summary>
        public List<SubjectInsightDto> Subjects { get; set; } = new();
        /// <summary>Up to 5 topics labelled NeedsAttention / PracticeRecommended / NeedsRetesting.</summary>
        public List<TopicInsightDto> WeakTopics { get; set; } = new();
        public int InsufficientDataTopicCount { get; set; }
        public List<TrendPointDto> ScoreTrend { get; set; } = new();
        public RevisionSummaryDto Revision { get; set; } = new();
        public PlanSummaryDto StudyPlan { get; set; } = new();
        public int CurrentStreak { get; set; }
        public int LongestStreak { get; set; }
        public int ActiveDaysLast7 { get; set; }
        public DataQualityDto DataQuality { get; set; } = new();
    }

    public class SubjectsResponseDto
    {
        public ProgressFiltersDto Filters { get; set; } = new();
        public List<SubjectInsightDto> Subjects { get; set; } = new();
        public DataQualityDto DataQuality { get; set; } = new();
    }

    public class TopicAttemptPointDto
    {
        public Guid AttemptId { get; set; }
        public DateTime Date { get; set; }
        public string TestKind { get; set; } = string.Empty;
        public int Attempted { get; set; }
        public int Correct { get; set; }
        public decimal? AccuracyPercent { get; set; }
    }

    public class TopicDetailDto
    {
        public TopicInsightDto Topic { get; set; } = new();
        /// <summary>Per-assessment accuracy for this topic, oldest first (last 20 assessments that contained it).</summary>
        public List<TopicAttemptPointDto> Trend { get; set; } = new();
        public int OpenMistakes { get; set; }
        public int RepeatedMistakes { get; set; }
        public int CorrectedMistakes { get; set; }
    }

    // ---------------------------------------------------------------- trends

    public class WeeklyPointDto
    {
        /// <summary>Monday of the IST week, yyyy-MM-dd.</summary>
        public string WeekStart { get; set; } = string.Empty;
        public int Attempted { get; set; }
        public int Correct { get; set; }
        public decimal? AccuracyPercent { get; set; }
        public decimal? AvgSecondsPerAnswered { get; set; }
    }

    public class TrendsDto
    {
        public ProgressFiltersDto Filters { get; set; } = new();
        public ChangeDto Last7Days { get; set; } = new();
        public ChangeDto Last30Days { get; set; } = new();
        /// <summary>Last 30 completed assessments, oldest first.</summary>
        public List<TrendPointDto> Attempts { get; set; } = new();
        /// <summary>Last 12 IST weeks, oldest first (weeks with no answers are omitted, not zero-filled).</summary>
        public List<WeeklyPointDto> Weekly { get; set; } = new();
    }

    // ---------------------------------------------------------------- per-assessment analytics

    public class AssessmentSummaryDto
    {
        public int TotalQuestions { get; set; }
        public int Attempted { get; set; }
        public int Correct { get; set; }
        public int Incorrect { get; set; }
        public int Unattempted { get; set; }
        public decimal Score { get; set; }
        /// <summary>One mark per question in the current marking model (see SMART_PROGRESS.md).</summary>
        public decimal MaxScore { get; set; }
        public decimal NegativeMarkingRatio { get; set; }
        public decimal? AccuracyPercent { get; set; }
        public decimal? ScorePercent { get; set; }
        public int TimeTakenSeconds { get; set; }
        public int AllottedSeconds { get; set; }
        public decimal? AvgSecondsPerAttempted { get; set; }
    }

    /// <summary>One topic INSIDE one assessment, plus how that topic stands over the student's whole history.</summary>
    public class AttemptTopicDto
    {
        public string Subject { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public bool IsUnclassified { get; set; }
        public int TotalQuestions { get; set; }
        public int Attempted { get; set; }
        public int Correct { get; set; }
        public int Incorrect { get; set; }
        public int Unattempted { get; set; }
        public decimal MarksObtained { get; set; }
        public decimal? AccuracyPercent { get; set; }
        public int TimedQuestions { get; set; }
        public int TimeSpentSeconds { get; set; }
        public decimal? AvgSecondsPerAttempted { get; set; }
        /// <summary>Status/confidence/recommendations come from the student's whole history of this topic, not just this one assessment.</summary>
        public TopicInsightDto? Overall { get; set; }
    }

    public class AttemptSubjectDto
    {
        public string Subject { get; set; } = string.Empty;
        public bool IsUnclassified { get; set; }
        public int TotalQuestions { get; set; }
        public int Attempted { get; set; }
        public int Correct { get; set; }
        public int Incorrect { get; set; }
        public int Unattempted { get; set; }
        public decimal MarksObtained { get; set; }
        public decimal MaxMarks { get; set; }
        public decimal? AccuracyPercent { get; set; }
        public int TimedQuestions { get; set; }
        public int TimeSpentSeconds { get; set; }
        public decimal? AvgSecondsPerAttempted { get; set; }
        public string Confidence { get; set; } = "Insufficient";
        /// <summary>Accuracy in the previous comparable assessment; null when there is none.</summary>
        public decimal? PreviousAccuracyPercent { get; set; }
        public int? PreviousAttempted { get; set; }
        public decimal? AccuracyChangePoints { get; set; }
        public List<AttemptTopicDto> Topics { get; set; } = new();
    }

    public class SlowQuestionDto
    {
        public int QuestionOrder { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public int Seconds { get; set; }
        public bool IsCorrect { get; set; }
        public bool WasSkipped { get; set; }
        public bool IsUnusuallySlow { get; set; }
    }

    public class SubjectTimeDto
    {
        public string Subject { get; set; } = string.Empty;
        public int TimedQuestions { get; set; }
        public int TimeSpentSeconds { get; set; }
        public decimal? AvgSecondsPerAttempted { get; set; }
    }

    public class TimeAnalysisDto
    {
        /// <summary>False when per-question timings were not recorded (older attempts, or too few reported).</summary>
        public bool Available { get; set; }
        /// <summary>Why it is unavailable, or a caveat when it is available.</summary>
        public string? Note { get; set; }
        public int CoveragePercent { get; set; }
        public int TrackedSeconds { get; set; }
        public decimal? MedianSecondsPerAttempted { get; set; }
        public decimal? AvgSecondsPerAttempted { get; set; }
        public decimal? AvgSecondsCorrect { get; set; }
        public decimal? AvgSecondsIncorrect { get; set; }
        public decimal? AllottedSecondsPerQuestion { get; set; }
        public decimal? PaceRatio { get; set; }
        /// <summary>OverAllottedPace | WithinAllottedPace | UnderAllottedPace -- always relative to THIS assessment's own time limit. Null when unknown.</summary>
        public string? PaceLabel { get; set; }
        public int FastIncorrectCount { get; set; }
        public decimal? FastIncorrectThresholdSeconds { get; set; }
        public List<SlowQuestionDto> SlowestQuestions { get; set; } = new();
        public List<SubjectTimeDto> BySubject { get; set; } = new();
    }

    public class SubjectChangeDto
    {
        public string Subject { get; set; } = string.Empty;
        public decimal? CurrentAccuracyPercent { get; set; }
        public int CurrentAttempted { get; set; }
        public decimal? PreviousAccuracyPercent { get; set; }
        public int PreviousAttempted { get; set; }
        public decimal? ChangePoints { get; set; }
    }

    public class ImprovementDto
    {
        /// <summary>True only when a genuinely comparable earlier assessment exists.</summary>
        public bool Comparable { get; set; }
        /// <summary>Why the two are comparable (or why there is no comparison).</summary>
        public string Basis { get; set; } = string.Empty;
        public Guid? PreviousAttemptId { get; set; }
        public DateTime? PreviousSubmittedAt { get; set; }
        public string? PreviousTitle { get; set; }
        public AssessmentSummaryDto? Previous { get; set; }
        public decimal? AccuracyChangePoints { get; set; }
        /// <summary>Null unless both assessments use the same negative-marking ratio.</summary>
        public decimal? ScorePercentChangePoints { get; set; }
        public string Note { get; set; } = string.Empty;
        public List<SubjectChangeDto> SubjectChanges { get; set; } = new();
        /// <summary>Last 30 days vs the 30 days before, for this assessment's type.</summary>
        public ChangeDto Last30Days { get; set; } = new();
    }

    public class AttemptAnalyticsDto
    {
        public Guid AttemptId { get; set; }
        public string TestKind { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
        public AssessmentSummaryDto Summary { get; set; } = new();
        public List<AttemptSubjectDto> Subjects { get; set; } = new();
        public TimeAnalysisDto Time { get; set; } = new();
        public ImprovementDto Improvement { get; set; } = new();
        public List<RecommendationDto> Recommendations { get; set; } = new();
        /// <summary>Limitations the screen should state (e.g. N questions have no topic).</summary>
        public List<string> Notes { get; set; } = new();
    }

    // ---------------------------------------------------------------- answer timings (recording)

    public class TestTimingItemDto
    {
        [Required] public Guid StudentAnswerId { get; set; }
        /// <summary>The client's RUNNING TOTAL of seconds spent on this question (not a delta).</summary>
        [Range(0, 86400)] public int TimeSpentSeconds { get; set; }
    }

    public class TestTimingsDto
    {
        [Required, MaxLength(300)] public List<TestTimingItemDto> Timings { get; set; } = new();
    }

    public class TestTimingsResponseDto
    {
        public int Accepted { get; set; }
        public int Ignored { get; set; }
    }

    // ---------------------------------------------------------------- Mistake Book / revision

    public class MistakeRevisionStateDto
    {
        public int Attempts { get; set; }
        public int Correct { get; set; }
        public int ConsecutiveCorrect { get; set; }
        public DateTime? LastRevisedAt { get; set; }
        public DateTime? NextDueAt { get; set; }
        public bool IsDue { get; set; }
    }

    public class MistakeItemDto
    {
        /// <summary>"qb:{guid}" (Question Bank) or "q:{guid}" (paper question) -- the stable key for every revision call.</summary>
        public string QuestionRef { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public bool IsUnclassified { get; set; }
        /// <summary>Plain-text preview (first ~300 chars) of the question as the student last saw it.</summary>
        public string QuestionPreview { get; set; } = string.Empty;
        public string? QuestionImageUrl { get; set; }
        public int TimesWrong { get; set; }
        public int TimesSkipped { get; set; }
        public int TimesCorrect { get; set; }
        public bool IsRepeated { get; set; }
        public bool IsMarkedForReview { get; set; }
        public DateTime LastMistakeAt { get; set; }
        public Guid? LastAttemptId { get; set; }
        public string? LastTestKind { get; set; }
        public string? LastSelectedOption { get; set; }
        /// <summary>New | Learning | Corrected | Dismissed</summary>
        public string Status { get; set; } = "New";
        /// <summary>Revision | Assessment -- how a Corrected mistake was corrected.</summary>
        public string? CorrectedBy { get; set; }
        public MistakeRevisionStateDto Revision { get; set; } = new();
    }

    public class MistakeBookDto
    {
        public List<MistakeItemDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        /// <summary>Counts over the WHOLE (unfiltered) Mistake Book, so filter chips can show badges.</summary>
        public RevisionSummaryDto Summary { get; set; } = new();
    }

    public class MistakeHistoryEntryDto
    {
        public Guid AttemptId { get; set; }
        public string TestKind { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime AttemptedAt { get; set; }
        public string? SelectedOption { get; set; }
        public bool IsCorrect { get; set; }
        public bool WasSkipped { get; set; }
    }

    public class RevisionHistoryEntryDto
    {
        public DateTime AnsweredAt { get; set; }
        public string SelectedOption { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }
    }

    /// <summary>Full review of one mistake (the answer key is fine to show: the student already submitted it).</summary>
    public class MistakeDetailDto
    {
        public MistakeItemDto Item { get; set; } = new();
        public string QuestionText { get; set; } = string.Empty;
        public string OptionA { get; set; } = string.Empty;
        public string OptionB { get; set; } = string.Empty;
        public string OptionC { get; set; } = string.Empty;
        public string OptionD { get; set; } = string.Empty;
        public string CorrectOption { get; set; } = string.Empty;
        public string? QuestionImageUrl { get; set; }
        public string? OptionAImageUrl { get; set; }
        public string? OptionBImageUrl { get; set; }
        public string? OptionCImageUrl { get; set; }
        public string? OptionDImageUrl { get; set; }
        public string? ExplanationImageUrl { get; set; }
        public List<ContentBlockDto> ContentBlocks { get; set; } = new();
        public string? Explanation { get; set; }
        public Guid? SourceQuestionId { get; set; }
        public Guid? SourceQuestionBankQuestionId { get; set; }
        public bool IsQuestionBank { get; set; }
        public List<MistakeHistoryEntryDto> History { get; set; } = new();
        public List<RevisionHistoryEntryDto> RevisionHistory { get; set; } = new();
    }

    /// <summary>A question handed out for revision: NO answer key (revealed by POST /api/revision/answers).</summary>
    public class RevisionQuestionDto
    {
        public string QuestionRef { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public string QuestionText { get; set; } = string.Empty;
        public string OptionA { get; set; } = string.Empty;
        public string OptionB { get; set; } = string.Empty;
        public string OptionC { get; set; } = string.Empty;
        public string OptionD { get; set; } = string.Empty;
        public string? QuestionImageUrl { get; set; }
        public string? OptionAImageUrl { get; set; }
        public string? OptionBImageUrl { get; set; }
        public string? OptionCImageUrl { get; set; }
        public string? OptionDImageUrl { get; set; }
        public List<ContentBlockDto> ContentBlocks { get; set; } = new();
        public int TimesWrong { get; set; }
        public bool IsRepeated { get; set; }
    }

    public class RevisionSessionDto
    {
        public List<RevisionQuestionDto> Questions { get; set; } = new();
        /// <summary>How many open mistakes matched the filters in total (the session is capped).</summary>
        public int MatchingOpenCount { get; set; }
    }

    public class RevisionAnswerRequestDto
    {
        [Required, MaxLength(60)] public string QuestionRef { get; set; } = string.Empty;
        [Required, MaxLength(5)] public string SelectedOption { get; set; } = string.Empty;
        [Range(0, 3600)] public int? TimeSpentSeconds { get; set; }
        /// <summary>Optional idempotency key -- a retry with the same id returns the first outcome.</summary>
        public Guid? ClientRequestId { get; set; }
    }

    public class RevisionAnswerResultDto
    {
        public bool IsCorrect { get; set; }
        public string CorrectOption { get; set; } = string.Empty;
        public string? Explanation { get; set; }
        public string? ExplanationImageUrl { get; set; }
        public string Status { get; set; } = "Learning";
        public int ConsecutiveCorrect { get; set; }
        public int CorrectionTarget { get; set; }
        public bool CorrectedNow { get; set; }
        public DateTime? NextDueAt { get; set; }
        /// <summary>True when this was a retried request already recorded earlier.</summary>
        public bool WasDuplicate { get; set; }
    }

    public class MistakeDismissDto
    {
        public bool Dismissed { get; set; }
    }

    // ---------------------------------------------------------------- study plan

    public class StudyTaskDto
    {
        public Guid Id { get; set; }
        /// <summary>ReviseMistakes | PracticeTopic | TimedSession</summary>
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public int TargetCount { get; set; }
        /// <summary>Live progress from real activity today (revision answers / answered questions / submitted assessments).</summary>
        public int ProgressCount { get; set; }
        /// <summary>Pending | Completed | Dismissed</summary>
        public string Status { get; set; } = "Pending";
        public string? Subject { get; set; }
        public string? Topic { get; set; }
        public int Priority { get; set; }
        public PracticeTargetDto? PracticeTarget { get; set; }
    }

    public class StudyPlanDto
    {
        /// <summary>IST calendar day, yyyy-MM-dd.</summary>
        public string Date { get; set; } = string.Empty;
        public PlanSummaryDto Summary { get; set; } = new();
        public List<StudyTaskDto> Tasks { get; set; } = new();
        /// <summary>Set when the plan is intentionally thin, e.g. a new student with no history.</summary>
        public string? Message { get; set; }
    }

    public class StudyPlanHistoryDayDto
    {
        public string Date { get; set; } = string.Empty;
        public PlanSummaryDto Summary { get; set; } = new();
        public List<StudyTaskDto> Tasks { get; set; } = new();
    }

    public class StudyPlanHistoryDto
    {
        public List<StudyPlanHistoryDayDto> Days { get; set; } = new();
    }

    public class StudyTaskUpdateDto
    {
        public bool Dismissed { get; set; }
    }

    // ---------------------------------------------------------------- Study Partner (opt-in sharing)

    public class StudyPlanShareSummaryDto
    {
        public string WeekStart { get; set; } = string.Empty;
        public int PlannedTasks { get; set; }
        public int CompletedTasks { get; set; }
        public decimal? CompletionPercent { get; set; }
        public int ActiveDays { get; set; }
    }
}
