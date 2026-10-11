using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Models;
using ScoramAPI.Services;
using ScoramAPI.Services.Analytics;
using Xunit;

namespace ScoramAPI.Tests;

/// <summary>Service-level tests on the EF InMemory provider (SQL-specific behaviour such as filtered unique
/// indexes is NOT exercised here -- run the migration against a SQL Server dev database for that).</summary>
public class ProgressServiceTests
{
    private static ScoramDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ScoramDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IOptions<AnalyticsOptions> Opts() => Options.Create(new AnalyticsOptions());

    private static (ProgressAnalyticsService Analytics, RevisionService Revision) Services(ScoramDbContext db)
    {
        var revision = new RevisionService(db, Opts());
        return (new ProgressAnalyticsService(db, revision, Opts()), revision);
    }

    private static StudentTestResult Attempt(Guid userId, TestAttemptStatus status, params (string Subject, string? Topic, OptionLetter? Picked, OptionLetter Correct)[] qs)
    {
        var r = new StudentTestResult
        {
            UserId = userId,
            TestKind = TestKind.Practice,
            Status = status,
            NegativeMarkingRatio = 0.25m,
            StartedAt = DateTime.UtcNow.AddMinutes(-20),
            AttemptedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        var order = 1;
        foreach (var q in qs)
        {
            r.Answers.Add(new StudentAnswer
            {
                QuestionBankQuestionId = Guid.NewGuid(),
                QuestionOrder = order++,
                SubjectSnapshot = q.Subject,
                TopicSnapshot = q.Topic,
                SelectedOption = q.Picked,
                CorrectOptionSnapshot = q.Correct,
                IsCorrect = q.Picked == q.Correct
            });
        }
        r.CorrectCount = r.Answers.Count(a => a.IsCorrect);
        r.WrongCount = r.Answers.Count(a => a.SelectedOption != null && !a.IsCorrect);
        r.SkippedCount = r.Answers.Count(a => a.SelectedOption == null);
        r.Score = r.CorrectCount - r.WrongCount * r.NegativeMarkingRatio;
        return r;
    }

    [Fact]
    public async Task AttemptAnalytics_OtherStudentsAttempt_LooksLikeNotFound()
    {
        using var db = NewDb();
        var owner = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        var a = Attempt(owner, TestAttemptStatus.Submitted, ("Quant", "Percentage", OptionLetter.A, OptionLetter.A));
        db.StudentTestResults.Add(a);
        await db.SaveChangesAsync();

        var (svc, _) = Services(db);
        Assert.Null(await svc.GetAttemptAnalyticsAsync(attacker, a.Id, default));
        Assert.NotNull(await svc.GetAttemptAnalyticsAsync(owner, a.Id, default));
    }

    [Fact]
    public async Task AttemptAnalytics_InProgressAttempt_IsNotAvailable()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        var a = Attempt(u, TestAttemptStatus.InProgress, ("Quant", "Percentage", null, OptionLetter.A));
        db.StudentTestResults.Add(a);
        await db.SaveChangesAsync();
        Assert.Null(await Services(db).Analytics.GetAttemptAnalyticsAsync(u, a.Id, default));
    }

    [Fact]
    public async Task AttemptAnalytics_CountsSkippedSeparately_AndAppliesNegativeMarking()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        var a = Attempt(u, TestAttemptStatus.Submitted,
            ("Quant", "Percentage", OptionLetter.A, OptionLetter.A),
            ("Quant", "Percentage", OptionLetter.B, OptionLetter.A),
            ("Quant", "Ratio", null, OptionLetter.C),
            ("Reasoning", null, OptionLetter.D, OptionLetter.D));
        db.StudentTestResults.Add(a);
        await db.SaveChangesAsync();

        var dto = (await Services(db).Analytics.GetAttemptAnalyticsAsync(u, a.Id, default))!;
        Assert.Equal(4, dto.Summary.TotalQuestions);
        Assert.Equal(3, dto.Summary.Attempted);
        Assert.Equal(2, dto.Summary.Correct);
        Assert.Equal(1, dto.Summary.Incorrect);
        Assert.Equal(1, dto.Summary.Unattempted);
        Assert.Equal(1.75m, dto.Summary.Score);
        Assert.Equal(66.7m, dto.Summary.AccuracyPercent);

        var quant = dto.Subjects.Single(s => s.Subject == "Quant");
        Assert.Equal(3, quant.TotalQuestions);
        Assert.Equal(0.75m, quant.MarksObtained);
        Assert.Contains(dto.Subjects, s => s.Subject == "Reasoning" && s.Topics.Single().IsUnclassified);
        Assert.DoesNotContain(dto.Subjects, s => s.Subject == "History"); // absent subjects are not zero rows
        Assert.False(dto.Time.Available);                                  // no timings recorded -> honest "unavailable"
        Assert.False(dto.Improvement.Comparable);
    }

    [Fact]
    public async Task Timings_OnlyOwnerAndOnlyWhileInProgress_AndNeverGoBackwards()
    {
        using var db = NewDb();
        var owner = Guid.NewGuid();
        var a = Attempt(owner, TestAttemptStatus.InProgress, ("Quant", "Percentage", null, OptionLetter.A));
        db.StudentTestResults.Add(a);
        await db.SaveChangesAsync();
        var answerId = a.Answers.Single().Id;
        var (svc, _) = Services(db);

        var body = (int secs) => new TestTimingsDto { Timings = new() { new TestTimingItemDto { StudentAnswerId = answerId, TimeSpentSeconds = secs } } };

        Assert.Equal(404, (await svc.RecordTimingsAsync(Guid.NewGuid(), a.Id, body(10), default)).StatusCode);

        var ok = await svc.RecordTimingsAsync(owner, a.Id, body(30), default);
        Assert.Equal(1, ok.Result!.Accepted);
        var lower = await svc.RecordTimingsAsync(owner, a.Id, body(10), default);
        Assert.Equal(1, lower.Result!.Ignored);
        Assert.Equal(30, (await db.StudentAnswers.AsNoTracking().SingleAsync()).TimeSpentSeconds);

        // Impossible: more seconds than have elapsed since the attempt started (20 min + grace).
        var absurd = await svc.RecordTimingsAsync(owner, a.Id, body(3000), default);
        Assert.Equal(0, absurd.Result!.Accepted);

        var tracked = await db.StudentTestResults.FirstAsync();
        tracked.Status = TestAttemptStatus.Submitted;
        await db.SaveChangesAsync();
        Assert.Equal(409, (await svc.RecordTimingsAsync(owner, a.Id, body(40), default)).StatusCode);
    }

    [Fact]
    public async Task Revision_AnswerIsIdempotent_AndNeverChangesTheOriginalAttempt()
    {
        using var db = NewDb();
        var u = Guid.NewGuid();
        var a = Attempt(u, TestAttemptStatus.Submitted, ("Quant", "Percentage", OptionLetter.B, OptionLetter.A));
        db.StudentTestResults.Add(a);
        await db.SaveChangesAsync();
        var key = new QuestionKey(a.Answers.Single().QuestionBankQuestionId, null);
        var (_, revision) = Services(db);
        var requestId = Guid.NewGuid();

        var first = await revision.AnswerAsync(u, key, OptionLetter.A, 12, requestId, default);
        var retry = await revision.AnswerAsync(u, key, OptionLetter.A, 12, requestId, default);

        Assert.True(first.Result!.IsCorrect);
        Assert.True(retry.Result!.WasDuplicate);
        Assert.Equal(1, await db.RevisionAttempts.CountAsync());

        var original = await db.StudentTestResults.Include(r => r.Answers).SingleAsync();
        Assert.Equal(0, original.CorrectCount);
        Assert.Equal(-0.25m, original.Score);
        Assert.False(original.Answers.Single().IsCorrect);
    }

    [Fact]
    public async Task Revision_CannotAnswerAnotherStudentsQuestion()
    {
        using var db = NewDb();
        var owner = Guid.NewGuid();
        var a = Attempt(owner, TestAttemptStatus.Submitted, ("Quant", "Percentage", OptionLetter.B, OptionLetter.A));
        db.StudentTestResults.Add(a);
        await db.SaveChangesAsync();
        var key = new QuestionKey(a.Answers.Single().QuestionBankQuestionId, null);

        var outcome = await Services(db).Revision.AnswerAsync(Guid.NewGuid(), key, OptionLetter.A, null, null, default);
        Assert.Equal(404, outcome.StatusCode);
        Assert.Empty(db.RevisionAttempts);
    }

    [Fact]
    public async Task Overview_WithNoHistory_ReturnsNullAccuracyNotZero()
    {
        using var db = NewDb();
        var dto = await Services(db).Analytics.GetOverviewAsync(Guid.NewGuid(), new ProgressFilter(), default);
        Assert.Null(dto.Totals.AccuracyPercent);
        Assert.Equal(0, dto.Totals.CompletedAssessments);
        Assert.Empty(dto.Subjects);
        Assert.Equal("NotEnoughData", dto.Last30Days.Status);
    }
}
