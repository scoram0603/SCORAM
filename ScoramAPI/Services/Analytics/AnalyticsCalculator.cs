using System.Globalization;

namespace ScoramAPI.Services.Analytics
{
    public enum TopicStatus
    {
        InsufficientData,
        NeedsAttention,
        PracticeRecommended,
        Improving,
        PerformingWell,
        NeedsRetesting
    }

    public enum Confidence
    {
        Insufficient,
        Preliminary,
        Higher
    }

    public enum ChangeStatus
    {
        Improved,
        Declined,
        Stable,
        NotEnoughData
    }

    /// <summary>Answered/correct counts for one slice of a student's history. Skipped questions are
    /// never part of this: accuracy is correct / answered.</summary>
    public readonly record struct WindowStats(int Attempted, int Correct)
    {
        public decimal? Accuracy => AnalyticsCalculator.Percent(Correct, Attempted);
        public static WindowStats operator +(WindowStats a, WindowStats b) => new(a.Attempted + b.Attempted, a.Correct + b.Correct);
    }

    /// <summary>One thing the student can do next, with the recorded numbers that justify it.
    /// ActionType is one of Practice | Revise | Retest | Review | None -- clients map it to a button.</summary>
    public sealed record Recommendation(string Code, string Title, string Reason, string ActionType);

    public sealed record TopicInputs(
        int Attempted,
        int Correct,
        WindowStats Recent,
        WindowStats Previous,
        DateTime? LastAttemptedAt,
        decimal? PaceRatio,
        int OpenMistakes,
        int RepeatedMistakes);

    public sealed record TopicVerdict(
        TopicStatus Status,
        Confidence Confidence,
        IReadOnlyList<Recommendation> Recommendations);

    public sealed record ChangeResult(ChangeStatus Status, decimal? ChangePoints, string Note);

    public sealed record MistakeStatusResult(string Status, int EffectiveConsecutiveCorrect, string? CorrectedBy);

    /// <summary>
    /// SMART PROGRESS -- the single authoritative, database-free calculation layer. Every number or
    /// label that the website, the Flutter app and Study Partner show about accuracy, change, topic
    /// strength, pace and revision state comes through here, so the two clients can never disagree.
    ///
    /// Metric definitions (also in SMART_PROGRESS.md):
    ///  * An attempt is COMPLETED once its status has left InProgress.
    ///  * A question is ANSWERED if an option was selected; skipped questions are not answered.
    ///  * Accuracy % = correct / answered. Null (never 0) when nothing was answered.
    ///  * Score % = score / total questions (the same definition the existing result screen uses).
    ///  * Change is ALWAYS reported in percentage POINTS. A relative % change is a different field.
    /// </summary>
    public static class AnalyticsCalculator
    {
        public static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

        // ------------------------------------------------------------------ basic maths

        /// <summary>numerator / denominator as a percentage rounded to 1 decimal place, or null when
        /// the denominator is zero (so "no data" can never be mistaken for "0%").</summary>
        public static decimal? Percent(int numerator, int denominator)
        {
            if (denominator <= 0) return null;
            return Math.Round((decimal)numerator / denominator * 100m, 1, MidpointRounding.AwayFromZero);
        }

        /// <summary>Marks for one slice of an attempt: +1 per correct, minus the attempt's negative
        /// marking ratio per wrong answer, 0 for skipped. Mirrors TestAttemptsController.GradeAttempt.</summary>
        public static decimal Marks(int correct, int wrong, decimal negativeMarkingRatio)
            => correct - (wrong * negativeMarkingRatio);

        /// <summary>current - previous, in percentage points (1 dp). Null if either side is unknown.</summary>
        public static decimal? ChangePoints(decimal? current, decimal? previous)
        {
            if (current == null || previous == null) return null;
            return Math.Round(current.Value - previous.Value, 1, MidpointRounding.AwayFromZero);
        }

        /// <summary>Relative change (new - old) / old, as a percentage. This is NOT a percentage-point
        /// difference. Null when the old value is missing or zero.</summary>
        public static decimal? RelativeChangePercent(decimal? previous, decimal? current)
        {
            if (previous == null || current == null || previous.Value == 0m) return null;
            return Math.Round((current.Value - previous.Value) / previous.Value * 100m, 1, MidpointRounding.AwayFromZero);
        }

        /// <summary>Median of the values (average of the two middle ones for an even count); null when empty.</summary>
        public static decimal? Median(IReadOnlyList<int> values)
        {
            if (values.Count == 0) return null;
            var sorted = values.OrderBy(v => v).ToList();
            var mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2m;
        }

        /// <summary>Whole seconds -> average per answered question, 1 dp; null when nothing answered.</summary>
        public static decimal? AverageSeconds(long totalSeconds, int count)
            => count <= 0 ? null : Math.Round((decimal)totalSeconds / count, 1, MidpointRounding.AwayFromZero);

        // ------------------------------------------------------------------ confidence / weak topics

        public static Confidence ConfidenceFor(int attempted, AnalyticsOptions o)
        {
            if (attempted < o.MinAttemptsForAnalysis) return Confidence.Insufficient;
            if (attempted < o.HigherConfidenceAttempts) return Confidence.Preliminary;
            return Confidence.Higher;
        }

        /// <summary>
        /// The transparent, rule-based Weak Topic Detector. A topic with too few answers is never forced
        /// into a verdict (InsufficientData). Otherwise: a clear recent gain = Improving; else the
        /// accuracy bands decide; a strong topic that has not been practised for a while =
        /// NeedsRetesting. Every recommendation carries the numbers it was based on.
        /// </summary>
        public static TopicVerdict Classify(TopicInputs i, AnalyticsOptions o, DateTime utcNow)
        {
            var confidence = ConfidenceFor(i.Attempted, o);
            var recs = new List<Recommendation>();

            if (confidence == Confidence.Insufficient)
            {
                recs.Add(new Recommendation(
                    "GATHER_DATA",
                    "Gather more attempts",
                    $"Only {i.Attempted} answered question{(i.Attempted == 1 ? "" : "s")} so far; at least {o.MinAttemptsForAnalysis} are needed before SCORAM labels this topic strong or weak.",
                    "Practice"));
                AddRevisionRecommendation(recs, i);
                return new TopicVerdict(TopicStatus.InsufficientData, confidence, recs);
            }

            var acc = Percent(i.Correct, i.Attempted) ?? 0m;
            var trendUsable = i.Recent.Attempted >= o.MinQuestionsPerComparisonWindow
                              && i.Previous.Attempted >= o.MinQuestionsPerComparisonWindow;
            decimal? delta = trendUsable ? ChangePoints(i.Recent.Accuracy, i.Previous.Accuracy) : null;

            TopicStatus status;
            if (delta.HasValue && delta.Value >= o.ImprovementDeltaPoints
                && (i.Recent.Accuracy ?? 0m) < o.PracticeRecommendedBelowPercent)
                status = TopicStatus.Improving;
            else if (acc < o.NeedsAttentionBelowPercent)
                status = TopicStatus.NeedsAttention;
            else if (acc < o.PracticeRecommendedBelowPercent)
                status = TopicStatus.PracticeRecommended;
            else if (i.LastAttemptedAt.HasValue && (utcNow - i.LastAttemptedAt.Value).TotalDays >= o.RetestAfterDays)
                status = TopicStatus.NeedsRetesting;
            else
                status = TopicStatus.PerformingWell;

            var basis = $"{Fmt(acc)}% over {i.Attempted} answered questions"
                        + (confidence == Confidence.Preliminary ? " (preliminary - based on a limited sample)" : "");

            switch (status)
            {
                case TopicStatus.NeedsAttention:
                    recs.Add(new Recommendation("REVIEW_AND_PRACTISE", "Review explanations and practise targeted questions",
                        $"Accuracy is {basis}, below the {Fmt(o.NeedsAttentionBelowPercent)}% line.", "Revise"));
                    break;
                case TopicStatus.PracticeRecommended:
                    recs.Add(new Recommendation("CONTINUE_PRACTICE", "Continue targeted practice",
                        $"Accuracy is {basis} - above {Fmt(o.NeedsAttentionBelowPercent)}% but still short of {Fmt(o.PracticeRecommendedBelowPercent)}%.", "Practice"));
                    break;
                case TopicStatus.Improving:
                    recs.Add(new Recommendation("KEEP_IMPROVING", "Keep up the targeted practice",
                        $"Accuracy rose {Fmt(delta ?? 0m)} percentage points (from {Fmt(i.Previous.Accuracy ?? 0m)}% to {Fmt(i.Recent.Accuracy ?? 0m)}%) between the last two {o.ComparisonWindowDays}-day windows; overall it is {basis}.", "Practice"));
                    break;
                case TopicStatus.NeedsRetesting:
                    var days = (int)Math.Floor((utcNow - i.LastAttemptedAt!.Value).TotalDays);
                    recs.Add(new Recommendation("RETEST", "Retest this topic",
                        $"Accuracy is {basis}, but you last practised it {days} days ago.", "Retest"));
                    break;
                default:
                    recs.Add(new Recommendation("MAINTAIN", "Maintain with occasional retests",
                        $"Accuracy is {basis}.", "Practice"));
                    break;
            }

            // Time-based advice only when a pace ratio exists (it needs per-question timings AND the
            // assessment's own allotted time -- never a made-up benchmark).
            if (i.PaceRatio.HasValue)
            {
                if (i.PaceRatio.Value > o.SlowPaceRatio && acc >= o.PracticeRecommendedBelowPercent)
                    recs.Add(new Recommendation("PRACTISE_TIMED", "Practise timed questions",
                        $"You are accurate here ({Fmt(acc)}%) but spend about {Fmt(i.PaceRatio.Value)}x the time allotted per question.", "Practice"));
                else if (i.PaceRatio.Value <= o.FastPaceRatio && acc < o.NeedsAttentionBelowPercent)
                    recs.Add(new Recommendation("SLOW_DOWN", "Focus on accuracy and careful reading",
                        $"You answer quickly (about {Fmt(i.PaceRatio.Value)}x the allotted time per question) but accuracy is {Fmt(acc)}%.", "Review"));
            }

            AddRevisionRecommendation(recs, i);
            return new TopicVerdict(status, confidence, recs);
        }

        private static void AddRevisionRecommendation(List<Recommendation> recs, TopicInputs i)
        {
            if (i.OpenMistakes <= 0) return;
            var repeated = i.RepeatedMistakes > 0
                ? $" {i.RepeatedMistakes} of them {(i.RepeatedMistakes == 1 ? "has" : "have")} been wrong more than once."
                : "";
            recs.Add(new Recommendation("REVISE_MISTAKES",
                $"Revise {i.OpenMistakes} open mistake{(i.OpenMistakes == 1 ? "" : "s")}",
                $"{i.OpenMistakes} question{(i.OpenMistakes == 1 ? "" : "s")} you got wrong in this topic {(i.OpenMistakes == 1 ? "has" : "have")} not been corrected yet.{repeated}",
                "Revise"));
        }

        // ------------------------------------------------------------------ comparisons

        /// <summary>
        /// Compares two windows of answered questions (recent vs previous). Reported in percentage
        /// points. If either window is too small the result is NotEnoughData - never a fabricated trend.
        /// </summary>
        public static ChangeResult Compare(WindowStats current, WindowStats previous, AnalyticsOptions o)
        {
            var min = o.MinQuestionsPerComparisonWindow;
            if (current.Attempted < min || previous.Attempted < min)
                return new ChangeResult(ChangeStatus.NotEnoughData, null, "Not enough comparable data yet.");

            var cur = (decimal)current.Correct / current.Attempted * 100m;
            var prev = (decimal)previous.Correct / previous.Attempted * 100m;
            var change = Math.Round(cur - prev, 1, MidpointRounding.AwayFromZero);

            var status = Math.Abs(change) < o.StableBandPoints
                ? ChangeStatus.Stable
                : change > 0 ? ChangeStatus.Improved : ChangeStatus.Declined;

            var sign = change > 0 ? "+" : "";
            var note = $"Accuracy {Fmt(Math.Round(prev, 1))}% -> {Fmt(Math.Round(cur, 1))}% ({sign}{Fmt(change)} percentage points).";
            return new ChangeResult(status, change, note);
        }

        // ------------------------------------------------------------------ pace

        /// <summary>Average seconds per answered question / seconds allotted per question by the
        /// assessment's own time limit. Null if either is unknown.</summary>
        public static decimal? PaceRatio(decimal? avgSecondsPerQuestion, decimal? allottedSecondsPerQuestion)
        {
            if (avgSecondsPerQuestion == null || allottedSecondsPerQuestion == null || allottedSecondsPerQuestion.Value <= 0m) return null;
            return Math.Round(avgSecondsPerQuestion.Value / allottedSecondsPerQuestion.Value, 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>Descriptive pace label relative to the assessment's own allotted time, or null when
        /// no ratio exists. Never labels a student "slow"/"fast" against an invented benchmark.</summary>
        public static string? PaceLabel(decimal? ratio, AnalyticsOptions o)
        {
            if (ratio == null) return null;
            if (ratio.Value > o.SlowPaceRatio) return "OverAllottedPace";
            if (ratio.Value <= o.FastPaceRatio) return "UnderAllottedPace";
            return "WithinAllottedPace";
        }

        // ------------------------------------------------------------------ revision / mistake book

        /// <summary>
        /// Derives a mistake's Mistake Book status from the student's own history:
        ///   Dismissed - the student hid it.
        ///   Corrected - answered correctly in a LATER assessment, or revised correctly N times in a row
        ///               since the latest mistake.
        ///   Learning  - revised at least once since the latest mistake but not yet corrected.
        ///   New       - not revised since the latest mistake.
        /// A fresh mistake in a later assessment resets Corrected/Learning back to New.
        /// </summary>
        public static MistakeStatusResult DeriveMistakeStatus(
            bool isDismissed,
            DateTime lastMistakeAt,
            DateTime? lastCorrectAt,
            int storedConsecutiveCorrect,
            DateTime? lastRevisedAt,
            DateTime? correctedAt,
            AnalyticsOptions o)
        {
            var revisedSinceMistake = lastRevisedAt.HasValue && lastRevisedAt.Value >= lastMistakeAt;
            var consecutive = revisedSinceMistake ? storedConsecutiveCorrect : 0;

            if (isDismissed) return new MistakeStatusResult("Dismissed", consecutive, null);

            if (lastCorrectAt.HasValue && lastCorrectAt.Value > lastMistakeAt)
                return new MistakeStatusResult("Corrected", consecutive, "Assessment");

            if (consecutive >= o.CorrectedAfterConsecutiveCorrect && correctedAt.HasValue && correctedAt.Value >= lastMistakeAt)
                return new MistakeStatusResult("Corrected", consecutive, "Revision");

            return new MistakeStatusResult(revisedSinceMistake ? "Learning" : "New", consecutive, null);
        }

        /// <summary>When a question is next due: immediately after a wrong answer, otherwise after the
        /// configured interval for the new streak length (last interval repeats).</summary>
        public static DateTime NextDueAt(DateTime utcNow, bool wasCorrect, int consecutiveCorrectAfter, AnalyticsOptions o)
        {
            if (!wasCorrect || consecutiveCorrectAfter <= 0 || o.RevisionIntervalDays.Length == 0) return utcNow;
            var idx = Math.Min(consecutiveCorrectAfter - 1, o.RevisionIntervalDays.Length - 1);
            return utcNow.AddDays(Math.Max(0, o.RevisionIntervalDays[idx]));
        }

        // ------------------------------------------------------------------ IST days

        public static DateOnly IstDay(DateTime utc) => DateOnly.FromDateTime(utc + Ist);

        /// <summary>[start, end) of an IST calendar day, expressed in UTC.</summary>
        public static (DateTime StartUtc, DateTime EndUtc) DayBoundsUtc(DateOnly istDay)
        {
            var start = DateTime.SpecifyKind(istDay.ToDateTime(TimeOnly.MinValue) - Ist, DateTimeKind.Utc);
            return (start, start.AddDays(1));
        }

        // ------------------------------------------------------------------ formatting

        /// <summary>Invariant, trailing-zero-free number text used inside recommendation reasons.</summary>
        public static string Fmt(decimal value) => value.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
