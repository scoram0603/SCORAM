namespace ScoramAPI.Services.Analytics
{
    /// <summary>
    /// SMART PROGRESS -- every threshold the analytics engine uses lives here (bound from the
    /// "Analytics" configuration section, see appsettings.json) so nothing is a magic number buried in
    /// a query or a React/Flutter screen. These are product defaults, not universal definitions of
    /// "mastery"; tune them through configuration without a code change.
    /// </summary>
    public sealed class AnalyticsOptions
    {
        public const string SectionName = "Analytics";

        // ---- Weak Topic Detector ----
        /// <summary>Fewer answered questions than this in a topic = InsufficientData (no weak/strong verdict).</summary>
        public int MinAttemptsForAnalysis { get; set; } = 5;
        /// <summary>This many answered questions or more = higher-confidence analysis (below it, "Preliminary").</summary>
        public int HigherConfidenceAttempts { get; set; } = 20;
        /// <summary>Accuracy below this % = NeedsAttention (high-priority revision candidate).</summary>
        public decimal NeedsAttentionBelowPercent { get; set; } = 50m;
        /// <summary>Accuracy from NeedsAttention up to (not including) this % = PracticeRecommended; at or above = PerformingWell.</summary>
        public decimal PracticeRecommendedBelowPercent { get; set; } = 70m;
        /// <summary>A well-performing topic not practised for this many days is flagged NeedsRetesting.</summary>
        public int RetestAfterDays { get; set; } = 21;
        /// <summary>Accuracy gain (percentage points, recent window vs the one before) that earns the "Improving" label.</summary>
        public decimal ImprovementDeltaPoints { get; set; } = 10m;

        // ---- Comparisons ----
        /// <summary>Length of the "recent" and "previous" windows used for topic/subject trends.</summary>
        public int ComparisonWindowDays { get; set; } = 30;
        /// <summary>Each window needs at least this many answered questions or the comparison is NotEnoughData.</summary>
        public int MinQuestionsPerComparisonWindow { get; set; } = 5;
        /// <summary>A change smaller than this (percentage points, either way) is reported as Stable.</summary>
        public decimal StableBandPoints { get; set; } = 2m;

        // ---- Time analysis ----
        /// <summary>Per-question time is only analysed when at least this % of answered questions carry a timing.</summary>
        public int MinTimingCoveragePercent { get; set; } = 60;
        /// <summary>Average seconds / allotted seconds per question above this = "over the allotted pace".</summary>
        public decimal SlowPaceRatio { get; set; } = 1.25m;
        /// <summary>Average seconds / allotted seconds per question at or below this = "well under the allotted pace".</summary>
        public decimal FastPaceRatio { get; set; } = 0.5m;
        /// <summary>A question that took more than this multiple of the allotted time is called out as unusually slow.</summary>
        public decimal UnusuallySlowMultiple { get; set; } = 2m;
        /// <summary>A wrong answer given within this fraction of the allotted time is a "fast but incorrect" response.</summary>
        public decimal FastIncorrectFraction { get; set; } = 0.25m;
        /// <summary>Hard ceiling for the seconds a client may report for ONE question (also capped by the test duration).</summary>
        public int MaxSecondsPerQuestion { get; set; } = 3600;

        // ---- Mistake Book / revision ----
        /// <summary>Consecutive correct revision answers before a mistake counts as Corrected.</summary>
        public int CorrectedAfterConsecutiveCorrect { get; set; } = 2;
        /// <summary>Days until the next revision after 1, 2, 3... consecutive correct answers (last value repeats).</summary>
        public int[] RevisionIntervalDays { get; set; } = new[] { 1, 3, 7, 14 };
        public int MaxSessionQuestions { get; set; } = 20;

        // ---- Study plan ----
        public int StudyPlanRevisionQuestions { get; set; } = 5;
        public int StudyPlanPracticeQuestions { get; set; } = 15;
        public int StudyPlanMaxPracticeTasks { get; set; } = 2;

        // ---- API bounds ----
        public int MaxPageSize { get; set; } = 50;
        public int MaxHistoryDays { get; set; } = 365;
    }
}
