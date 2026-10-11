using ScoramAPI.Services.Analytics;
using Xunit;

namespace ScoramAPI.Tests;

public class AnalyticsCalculatorTests
{
    private static readonly AnalyticsOptions O = new();
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Percent_ZeroDenominator_IsNullNotZero()
    {
        Assert.Null(AnalyticsCalculator.Percent(0, 0));
        Assert.Null(AnalyticsCalculator.Percent(3, -1));
    }

    [Theory]
    [InlineData(12, 20, 60.0)]
    [InlineData(1, 3, 33.3)]
    [InlineData(0, 5, 0.0)]
    public void Percent_RoundsToOneDecimal(int c, int n, double expected)
        => Assert.Equal((decimal)expected, AnalyticsCalculator.Percent(c, n));

    [Fact]
    public void Marks_AppliesNegativeMarkingOnlyToWrongAnswers()
    {
        Assert.Equal(7.5m, AnalyticsCalculator.Marks(8, 2, 0.25m));
        Assert.Equal(8m, AnalyticsCalculator.Marks(8, 2, 0m));
        Assert.Equal(-0.25m, AnalyticsCalculator.Marks(0, 1, 0.25m));
    }

    [Fact]
    public void ChangePoints_IsDifferenceNotRelative()
    {
        Assert.Equal(14m, AnalyticsCalculator.ChangePoints(62m, 48m));
        Assert.Equal(29.2m, AnalyticsCalculator.RelativeChangePercent(48m, 62m));
        Assert.Null(AnalyticsCalculator.ChangePoints(null, 48m));
        Assert.Null(AnalyticsCalculator.RelativeChangePercent(0m, 10m));
    }

    [Fact]
    public void Median_HandlesEvenOddAndEmpty()
    {
        Assert.Null(AnalyticsCalculator.Median(Array.Empty<int>()));
        Assert.Equal(5m, AnalyticsCalculator.Median(new[] { 9, 1, 5 }));
        Assert.Equal(6m, AnalyticsCalculator.Median(new[] { 4, 8, 1, 9 }));
    }

    [Theory]
    [InlineData(0, TopicStatus.InsufficientData)]
    [InlineData(4, TopicStatus.InsufficientData)]
    public void Classify_TooFewAttempts_NeverForcesAVerdict(int attempted, TopicStatus expected)
    {
        var v = AnalyticsCalculator.Classify(Inputs(attempted, 0), O, Now);
        Assert.Equal(expected, v.Status);
        Assert.Equal(Confidence.Insufficient, v.Confidence);
    }

    [Theory]
    [InlineData(10, 4, TopicStatus.NeedsAttention)]       // 40%
    [InlineData(10, 5, TopicStatus.PracticeRecommended)]  // 50% boundary
    [InlineData(10, 7, TopicStatus.PerformingWell)]       // 70% boundary
    public void Classify_UsesConfiguredBands(int attempted, int correct, TopicStatus expected)
        => Assert.Equal(expected, AnalyticsCalculator.Classify(Inputs(attempted, correct), O, Now).Status);

    [Fact]
    public void Classify_ConfidenceBands()
    {
        Assert.Equal(Confidence.Preliminary, AnalyticsCalculator.Classify(Inputs(5, 3), O, Now).Confidence);
        Assert.Equal(Confidence.Higher, AnalyticsCalculator.Classify(Inputs(20, 14), O, Now).Confidence);
    }

    [Fact]
    public void Classify_StrongButStale_NeedsRetesting()
    {
        var i = Inputs(10, 9) with { LastAttemptedAt = Now.AddDays(-30) };
        Assert.Equal(TopicStatus.NeedsRetesting, AnalyticsCalculator.Classify(i, O, Now).Status);
    }

    [Fact]
    public void Classify_BigRecentGainBelowTarget_IsImproving()
    {
        var i = Inputs(20, 9) with { Recent = new WindowStats(10, 6), Previous = new WindowStats(10, 3) }; // 60% vs 30%
        Assert.Equal(TopicStatus.Improving, AnalyticsCalculator.Classify(i, O, Now).Status);
    }

    [Fact]
    public void Classify_FastButWrong_RecommendsAccuracyNotSpeed()
    {
        var i = Inputs(10, 3) with { PaceRatio = 0.3m };
        var codes = AnalyticsCalculator.Classify(i, O, Now).Recommendations.Select(r => r.Code).ToList();
        Assert.Contains("SLOW_DOWN", codes);
        Assert.DoesNotContain("PRACTISE_TIMED", codes);
    }

    [Fact]
    public void Classify_AccurateButSlow_RecommendsTimedPractice()
    {
        var i = Inputs(10, 9) with { PaceRatio = 1.6m };
        Assert.Contains(AnalyticsCalculator.Classify(i, O, Now).Recommendations, r => r.Code == "PRACTISE_TIMED");
    }

    [Fact]
    public void Compare_TooFewQuestionsInEitherWindow_IsNotEnoughData()
    {
        var r = AnalyticsCalculator.Compare(new WindowStats(4, 3), new WindowStats(20, 10), O);
        Assert.Equal(ChangeStatus.NotEnoughData, r.Status);
        Assert.Null(r.ChangePoints);
    }

    [Fact]
    public void Compare_ReportsPercentagePointsAndStableBand()
    {
        var up = AnalyticsCalculator.Compare(new WindowStats(50, 31), new WindowStats(50, 24), O); // 62 vs 48
        Assert.Equal(ChangeStatus.Improved, up.Status);
        Assert.Equal(14m, up.ChangePoints);

        var flat = AnalyticsCalculator.Compare(new WindowStats(100, 51), new WindowStats(100, 50), O);
        Assert.Equal(ChangeStatus.Stable, flat.Status);

        var down = AnalyticsCalculator.Compare(new WindowStats(10, 4), new WindowStats(10, 8), O);
        Assert.Equal(ChangeStatus.Declined, down.Status);
    }

    [Fact]
    public void PaceRatio_NullWithoutAllottedTime_AndNoLabel()
    {
        Assert.Null(AnalyticsCalculator.PaceRatio(40m, null));
        Assert.Null(AnalyticsCalculator.PaceRatio(40m, 0m));
        Assert.Null(AnalyticsCalculator.PaceLabel(null, O));
        Assert.Equal("OverAllottedPace", AnalyticsCalculator.PaceLabel(1.5m, O));
        Assert.Equal("UnderAllottedPace", AnalyticsCalculator.PaceLabel(0.4m, O));
        Assert.Equal("WithinAllottedPace", AnalyticsCalculator.PaceLabel(1.0m, O));
    }

    [Fact]
    public void MistakeStatus_NewLearningCorrectedDismissed_AndReset()
    {
        var mistake = Now.AddDays(-5);
        Assert.Equal("New", AnalyticsCalculator.DeriveMistakeStatus(false, mistake, null, 0, null, null, O).Status);
        Assert.Equal("Learning", AnalyticsCalculator.DeriveMistakeStatus(false, mistake, null, 1, mistake.AddDays(1), null, O).Status);
        Assert.Equal("Corrected", AnalyticsCalculator.DeriveMistakeStatus(false, mistake, null, 2, mistake.AddDays(2), mistake.AddDays(2), O).Status);
        Assert.Equal("Corrected", AnalyticsCalculator.DeriveMistakeStatus(false, mistake, mistake.AddDays(1), 0, null, null, O).Status); // right in a later test
        Assert.Equal("Dismissed", AnalyticsCalculator.DeriveMistakeStatus(true, mistake, null, 0, null, null, O).Status);

        // A fresh mistake AFTER an earlier correction puts it back to New.
        var fresh = AnalyticsCalculator.DeriveMistakeStatus(false, Now, mistake.AddDays(1), 2, mistake.AddDays(2), mistake.AddDays(2), O);
        Assert.Equal("New", fresh.Status);
    }

    [Fact]
    public void NextDue_WrongIsImmediate_CorrectUsesIntervals_LastIntervalRepeats()
    {
        Assert.Equal(Now, AnalyticsCalculator.NextDueAt(Now, false, 0, O));
        Assert.Equal(Now.AddDays(1), AnalyticsCalculator.NextDueAt(Now, true, 1, O));
        Assert.Equal(Now.AddDays(14), AnalyticsCalculator.NextDueAt(Now, true, 4, O));
        Assert.Equal(Now.AddDays(14), AnalyticsCalculator.NextDueAt(Now, true, 9, O));
    }

    [Fact]
    public void IstDay_UsesIndianCalendarDay()
    {
        // 19:00 UTC on the 10th is 00:30 IST on the 11th.
        var utc = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateOnly(2026, 10, 11), AnalyticsCalculator.IstDay(utc));
        var (start, end) = AnalyticsCalculator.DayBoundsUtc(new DateOnly(2026, 10, 11));
        Assert.Equal(new DateTime(2026, 10, 10, 18, 30, 0, DateTimeKind.Utc), start);
        Assert.Equal(start.AddDays(1), end);
    }

    private static TopicInputs Inputs(int attempted, int correct) =>
        new(attempted, correct, new WindowStats(0, 0), new WindowStats(0, 0), Now.AddDays(-1), null, 0, 0);
}
