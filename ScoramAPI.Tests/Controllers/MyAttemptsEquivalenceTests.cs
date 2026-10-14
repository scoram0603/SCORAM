using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using ScoramAPI.Controllers;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Models;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Controllers
{
    // PROOF that the optimized GET /api/tests/attempts/mine returns exactly what it returned before.
    //
    // The optimization (see TestAttemptsController.Mine) stopped loading every answer row for every
    // attempt on the page: PercentageScore now uses a grouped COUNT query, and the full answers are loaded
    // only for the rare "Quiz attempt without a Quiz row" whose title is derived from answer subjects.
    //
    // ReferenceMine below is the PREVIOUS implementation, copied verbatim (tracked entity query with
    // Include(r => r.Answers), then the same mapping). Each test seeds one in-memory database, runs the
    // controller and the reference against it, and requires byte-identical JSON.
    //
    // Limits (honest): the in-memory provider is not SQL Server. It proves the mapping, counting, paging,
    // filtering and title logic are unchanged; it cannot prove SQL-level row ordering for ties on
    // StartedAt, which neither the old nor the new query defined.
    public class MyAttemptsEquivalenceTests : IDisposable
    {
        private readonly string _dbName = Guid.NewGuid().ToString();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Guid _otherUserId = Guid.NewGuid();
        private readonly DateTime _t0 = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

        private ScoramDbContext NewContext() => new(
            new DbContextOptionsBuilder<ScoramDbContext>().UseInMemoryDatabase(_dbName).Options);

        public void Dispose()
        {
            using var db = NewContext();
            db.Database.EnsureDeleted();
        }

        // ---- fixtures -------------------------------------------------------------------------------

        private async Task SeedAsync()
        {
            using var db = NewContext();

            var mock = new MockTest { Title = "SSC Mock 1", ExamName = "SSC CGL" };
            var template = new PracticeTestTemplate { Title = "Algebra Basics" };
            var exam = new Exam { Name = "SSC CGL" };
            var paper = new Paper { ExamId = exam.Id, Exam = exam, Year = 2022, PaperCode = "T1" };
            var quiz = new Quiz { Title = "Daily Quiz" };
            db.AddRange(mock, template, exam, paper, quiz);

            StudentTestResult Result(TestKind kind, TestAttemptStatus status, int minutes, Action<StudentTestResult>? tweak = null)
            {
                var r = new StudentTestResult
                {
                    UserId = _userId,
                    TestKind = kind,
                    Status = status,
                    StartedAt = _t0.AddMinutes(minutes),
                    AttemptedAt = _t0.AddMinutes(minutes + 5),
                    Score = status == TestAttemptStatus.InProgress ? 0 : 7.5m,
                    CorrectCount = 4,
                    WrongCount = 2,
                    SkippedCount = 1,
                    TimeTakenSeconds = 300 + minutes
                };
                tweak?.Invoke(r);
                db.StudentTestResults.Add(r);
                return r;
            }

            void Answers(StudentTestResult r, params string?[] subjects)
            {
                for (var i = 0; i < subjects.Length; i++)
                    db.StudentAnswers.Add(new StudentAnswer
                    {
                        StudentTestResultId = r.Id,
                        QuestionOrder = i + 1,
                        QuestionTextSnapshot = "Q" + i,
                        SubjectSnapshot = subjects[i],
                        ContentBlocksJsonSnapshot = "[{\"type\":\"text\",\"text\":\"heavy payload\"}]"
                    });
            }

            var mockAttempt = Result(TestKind.Mock, TestAttemptStatus.Submitted, 1, r => { r.MockTestId = mock.Id; });
            Answers(mockAttempt, "Maths", "Maths", "Reasoning");

            var templated = Result(TestKind.Practice, TestAttemptStatus.InProgress, 2, r => { r.PracticeTestTemplateId = template.Id; });
            Answers(templated, "Maths", "English");

            Result(TestKind.Practice, TestAttemptStatus.Submitted, 3); // ad-hoc, zero answers -> 0 %

            var pyp = Result(TestKind.PreviousYearPaper, TestAttemptStatus.AutoSubmitted, 4, r => { r.PaperId = paper.Id; });
            Answers(pyp, "Maths", "Maths", "Maths", "English", "GK");

            var withQuiz = Result(TestKind.Quiz, TestAttemptStatus.Submitted, 5, r => { r.QuizId = quiz.Id; });
            Answers(withQuiz, "Reasoning", "Reasoning", "Reasoning", "Reasoning");

            var quizNoRow = Result(TestKind.Quiz, TestAttemptStatus.Submitted, 6);
            Answers(quizNoRow, "Maths", "Maths", "Reasoning", "English");

            var quizNoRowNoSubjects = Result(TestKind.Quiz, TestAttemptStatus.Expired, 7);
            Answers(quizNoRowNoSubjects, null, "  ");

            var quizNoRowInProgress = Result(TestKind.Quiz, TestAttemptStatus.InProgress, 8);
            Answers(quizNoRowInProgress, "History");

            // Someone else's attempt must never appear.
            db.StudentTestResults.Add(new StudentTestResult
            {
                UserId = _otherUserId, TestKind = TestKind.Mock, Status = TestAttemptStatus.Submitted, StartedAt = _t0.AddMinutes(99)
            });

            await db.SaveChangesAsync();
        }

        private TestAttemptsController NewController(ScoramDbContext db, Guid userId) => new(
            db, Mock.Of<IGamificationService>(), Mock.Of<INotificationService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "test"))
                }
            }
        };

        // ---- the previous implementation, verbatim (reference) ---------------------------------------

        private static async Task<PagedResult<MyTestAttemptSummaryDto>> ReferenceMine(
            ScoramDbContext db, Guid userId, string? status, int page, int pageSize)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = db.StudentTestResults
                .Include(r => r.Answers)
                .Include(r => r.MockTest)
                .Include(r => r.PracticeTestTemplate)
                .Include(r => r.Paper).ThenInclude(p => p!.Exam)
                .Include(r => r.Quiz)
                .Where(r => r.UserId == userId)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TestAttemptStatus>(status, true, out var parsedStatus))
                query = query.Where(r => r.Status == parsedStatus);

            query = query.OrderByDescending(r => r.StartedAt);

            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var mapped = new List<MyTestAttemptSummaryDto>();
            foreach (var r in items)
            {
                mapped.Add(new MyTestAttemptSummaryDto
                {
                    AttemptId = r.Id,
                    TestKind = r.TestKind.ToString(),
                    Title = TestAttemptsController.TitleFor(r),
                    Status = r.Status.ToString(),
                    Score = r.Status == TestAttemptStatus.InProgress ? null : r.Score,
                    PercentageScore = RefPercentage(r),
                    AccuracyPercent = RefAccuracy(r),
                    TimeTakenSeconds = r.TimeTakenSeconds,
                    StartedAt = r.StartedAt,
                    SubmittedAt = r.Status == TestAttemptStatus.InProgress ? null : r.AttemptedAt,
                    CanResume = r.Status == TestAttemptStatus.InProgress
                });
            }

            return new PagedResult<MyTestAttemptSummaryDto> { Items = mapped, TotalCount = totalCount, Page = page, PageSize = pageSize };
        }

        private static decimal? RefPercentage(StudentTestResult r)
        {
            if (r.Status == TestAttemptStatus.InProgress) return null;
            var total = r.Answers.Count;
            return total == 0 ? 0 : Math.Round(r.Score / total * 100, 2);
        }

        private static decimal? RefAccuracy(StudentTestResult r)
        {
            if (r.Status == TestAttemptStatus.InProgress) return null;
            var attempted = r.CorrectCount + r.WrongCount;
            return attempted == 0 ? 0 : Math.Round((decimal)r.CorrectCount / attempted * 100, 2);
        }

        // ---- assertions ------------------------------------------------------------------------------

        private async Task AssertSameAsync(Guid userId, string? status, int page, int pageSize)
        {
            using var dbNew = NewContext();
            using var dbRef = NewContext();

            var actionResult = await NewController(dbNew, userId).Mine(status, page, pageSize);
            var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
            var actual = Assert.IsType<PagedResult<MyTestAttemptSummaryDto>>(ok.Value);
            var expected = await ReferenceMine(dbRef, userId, status, page, pageSize);

            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        }

        [Fact]
        public async Task AllAttempts_FirstPage_AreIdenticalToThePreviousImplementation()
        {
            await SeedAsync();
            await AssertSameAsync(_userId, null, 1, 20);
        }

        [Theory]
        [InlineData(1, 3)]
        [InlineData(2, 3)]
        [InlineData(3, 3)]
        [InlineData(4, 3)] // past the end -> empty page
        [InlineData(1, 1)]
        [InlineData(0, 0)]       // clamped to page 1 / size 1
        [InlineData(1, 100000)]  // clamped to 100
        [InlineData(-5, 20)]
        public async Task Paging_AndClamping_AreIdentical(int page, int pageSize)
        {
            await SeedAsync();
            await AssertSameAsync(_userId, null, page, pageSize);
        }

        [Theory]
        [InlineData("Submitted")]
        [InlineData("inprogress")]
        [InlineData("AutoSubmitted")]
        [InlineData("Expired")]
        [InlineData("not-a-status")] // ignored by both implementations
        [InlineData("")]
        public async Task StatusFilter_IsIdentical(string status)
        {
            await SeedAsync();
            await AssertSameAsync(_userId, status, 1, 20);
        }

        [Fact]
        public async Task UserWithNoAttempts_GetsTheSameEmptyPage()
        {
            await SeedAsync();
            await AssertSameAsync(Guid.NewGuid(), null, 1, 20);
        }

        [Fact]
        public async Task OnlyTheCallersOwnAttempts_AreReturned()
        {
            await SeedAsync();
            using var db = NewContext();
            var ok = Assert.IsType<OkObjectResult>((await NewController(db, _userId).Mine(null, 1, 100)).Result);
            var page = Assert.IsType<PagedResult<MyTestAttemptSummaryDto>>(ok.Value);
            Assert.Equal(8, page.TotalCount);
            Assert.Equal(8, page.Items.Count);
        }

        [Fact]
        public async Task QuizWithoutAQuizRow_IsStillTitledFromItsAnswerSubjects()
        {
            await SeedAsync();
            using var db = NewContext();
            var ok = Assert.IsType<OkObjectResult>((await NewController(db, _userId).Mine(null, 1, 100)).Result);
            var items = Assert.IsType<PagedResult<MyTestAttemptSummaryDto>>(ok.Value).Items;

            Assert.Contains(items, i => i.Title == "Maths & Reasoning Quiz");   // derived from subjects (first two distinct)
            Assert.Contains(items, i => i.Title == "Daily Quiz");                // has a Quiz row
            Assert.Contains(items, i => i.Title == "Quiz");                      // no usable subjects -> generic fallback
            Assert.Contains(items, i => i.Title == "History Quiz");              // in-progress, one subject
            Assert.Contains(items, i => i.Title == "Algebra Basics");
            Assert.Contains(items, i => i.Title == "SSC Mock 1");
            Assert.Contains(items, i => i.Title == "SSC CGL – 2022 – T1");
        }

        [Fact]
        public async Task PercentageScore_UsesTheDatabaseAnswerCount()
        {
            await SeedAsync();
            using var db = NewContext();
            var ok = Assert.IsType<OkObjectResult>((await NewController(db, _userId).Mine("Submitted", 1, 100)).Result);
            var items = Assert.IsType<PagedResult<MyTestAttemptSummaryDto>>(ok.Value).Items;

            // Mock attempt: score 7.5 over 3 answers -> 250 %; ad-hoc practice with no answers -> 0 %.
            Assert.Contains(items, i => i.Title == "SSC Mock 1" && i.PercentageScore == 250m);
            Assert.Contains(items, i => i.Title == "Practice Test" && i.PercentageScore == 0m);
        }

        [Fact]
        public async Task TheOptimizedQuery_DoesNotLeaveEntitiesTrackedOnTheContext()
        {
            await SeedAsync();
            using var db = NewContext();
            await NewController(db, _userId).Mine(null, 1, 100);
            Assert.Empty(db.ChangeTracker.Entries());
        }
    }
}
