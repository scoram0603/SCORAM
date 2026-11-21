// Written without a .NET SDK available; not yet executed.
using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using ScoramAPI.Controllers;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Models;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Controllers
{
    // PERFORMANCE (audit P2 API-08, round 5): GetById / Today (Question Bank), the vote endpoint and the legacy
    // QuestionsController.GetById used to run their counts / my-vote / bookmark checks as 3-6 sequential round
    // trips; they now run as one projection (or one grouped query). These tests pin the VALUES to hand-computed
    // numbers for every viewer type, so the merge cannot have changed what any caller sees, and cover the new
    // gate around Today's once-per-day pick.
    //
    // Limits (honest): the in-memory provider proves the numbers, not the SQL, the plan or any timing; the
    // command-count claims in the report (10 -> 4, 8 -> 3) must be measured on staging.
    public class QuestionBankEngagementTests : IDisposable
    {
        private readonly string _dbName = Guid.NewGuid().ToString();
        private readonly Exam _exam = new() { Name = "Engagement exam" };
        private readonly QuestionBankSubject _subject = new() { Name = "S" };
        private readonly QuestionBankTopic _topic;

        private QuestionBankQuestion _busy = null!, _quiet = null!;
        private readonly Guid _liker1 = Guid.NewGuid(), _liker2 = Guid.NewGuid(), _liker3 = Guid.NewGuid();
        private readonly Guid _disliker1 = Guid.NewGuid(), _disliker2 = Guid.NewGuid();
        private readonly Guid _bystander = Guid.NewGuid();

        public QuestionBankEngagementTests()
        {
            _topic = new() { Name = "T", SubjectId = _subject.Id };
            Seed();
        }

        public void Dispose()
        {
            using var db = NewContext();
            db.Database.EnsureDeleted();
        }

        private ScoramDbContext NewContext() => new(
            new DbContextOptionsBuilder<ScoramDbContext>().UseInMemoryDatabase(_dbName).Options);

        private void Seed()
        {
            using var db = NewContext();
            db.AddRange(_exam, _subject, _topic);
            _busy = new QuestionBankQuestion { QuestionText = "busy", SubjectId = _subject.Id, TopicId = _topic.Id, CreatedAt = DateTime.UtcNow.AddMinutes(-2) };
            _quiet = new QuestionBankQuestion { QuestionText = "quiet", SubjectId = _subject.Id, TopicId = _topic.Id, CreatedAt = DateTime.UtcNow.AddMinutes(-1) };
            db.QuestionBankQuestions.AddRange(_busy, _quiet);
            db.QuestionBankExamMappings.Add(new QuestionBankExamMapping { QuestionBankQuestionId = _busy.Id, ExamId = _exam.Id, Year = 2020 });
            db.QuestionBankExamMappings.Add(new QuestionBankExamMapping { QuestionBankQuestionId = _quiet.Id, ExamId = _exam.Id, Year = 2021 });

            // _busy: 3 likes, 2 dislikes, 2 comments, 1 approved + 1 unapproved solution
            foreach (var u in new[] { _liker1, _liker2, _liker3 })
                db.QuestionVotes.Add(new QuestionVote { QuestionBankQuestionId = _busy.Id, UserId = u, IsLike = true });
            foreach (var u in new[] { _disliker1, _disliker2 })
                db.QuestionVotes.Add(new QuestionVote { QuestionBankQuestionId = _busy.Id, UserId = u, IsLike = false });
            db.QuestionComments.Add(new QuestionComment { QuestionBankQuestionId = _busy.Id, CommentText = "c1" });
            db.QuestionComments.Add(new QuestionComment { QuestionBankQuestionId = _busy.Id, CommentText = "c2" });
            db.QuestionSolutions.Add(new QuestionSolution { QuestionBankQuestionId = _busy.Id, Title = "approved", IsApproved = true });
            db.QuestionSolutions.Add(new QuestionSolution { QuestionBankQuestionId = _busy.Id, Title = "pending", IsApproved = false });
            // bookmarks: liker1 bookmarked _busy; the bystander bookmarked _quiet (must never show on _busy)
            db.Bookmarks.Add(new Bookmark { UserId = _liker1, QuestionBankQuestionId = _busy.Id });
            db.Bookmarks.Add(new Bookmark { UserId = _bystander, QuestionBankQuestionId = _quiet.Id });
            // a vote on _quiet by a disliker must never leak into _busy
            db.QuestionVotes.Add(new QuestionVote { QuestionBankQuestionId = _quiet.Id, UserId = _disliker1, IsLike = true });
            db.SaveChanges();
        }

        // ---- controller plumbing (same shape as QuestionBankEquivalenceTests.NewController) ------------

        private QuestionBankController NewController(ScoramDbContext db, MyExamScope scope, Guid? userId = null, IMemoryCache? cache = null)
        {
            var scopeSvc = new Mock<IMyExamScopeService>();
            scopeSvc.Setup(s => s.GetScopeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>())).ReturnsAsync(scope);
            return new QuestionBankController(
                db, Mock.Of<IGamificationService>(), Mock.Of<ILogger<QuestionBankController>>(), scopeSvc.Object,
                new ExamTaxonomyService(db, Mock.Of<ILogger<ExamTaxonomyService>>()),
                cache ?? new MemoryCache(new MemoryCacheOptions()))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(userId) } }
            };
        }

        private static ClaimsPrincipal Principal(Guid? userId) => new(userId.HasValue
            ? new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()) }, "test")
            : new ClaimsIdentity());

        private static T Ok<T>(ActionResult<T> result) => (T)((OkObjectResult)result.Result!).Value!;

        private MyExamScope OwnScope() => new(true, new[] { _exam.Id }, new[] { _exam.Name });

        // ---- QuestionBankController.GetById -------------------------------------------------------------

        [Fact]
        public async Task GetById_Anonymous_SeesCounts_ButNoMyVoteAndNoBookmark()
        {
            using var db = NewContext();
            var dto = Ok(await NewController(db, MyExamScope.Unscoped).GetById(_busy.Id));
            Assert.Equal((3, 2, 2, 1), (dto.LikeCount, dto.DislikeCount, dto.CommentCount, dto.SolutionCount)); // unapproved solution excluded
            Assert.Null(dto.MyVote);
            Assert.False(dto.IsBookmarked);   // liker1's bookmark must not show to an anonymous caller
        }

        [Fact]
        public async Task GetById_MyVoteAndBookmark_ArePerCaller()
        {
            using var db = NewContext();

            var liker = Ok(await NewController(db, MyExamScope.Unscoped, _liker1).GetById(_busy.Id));
            Assert.Equal(((bool?)true, true), (liker.MyVote, liker.IsBookmarked));

            var disliker = Ok(await NewController(db, MyExamScope.Unscoped, _disliker2).GetById(_busy.Id));
            Assert.Equal(((bool?)false, false), (disliker.MyVote, disliker.IsBookmarked));

            var stranger = Ok(await NewController(db, MyExamScope.Unscoped, _bystander).GetById(_busy.Id));
            Assert.Null(stranger.MyVote);          // never voted on _busy
            Assert.False(stranger.IsBookmarked);   // bookmarked a DIFFERENT question

            // counts are the same for every viewer
            foreach (var d in new[] { liker, disliker, stranger })
                Assert.Equal((3, 2, 2, 1), (d.LikeCount, d.DislikeCount, d.CommentCount, d.SolutionCount));
        }

        [Fact]
        public async Task GetById_QuestionWithOnlyForeignEngagement_ReturnsZeros()
        {
            using var db = NewContext();
            var dto = Ok(await NewController(db, MyExamScope.Unscoped, _bystander).GetById(_quiet.Id));
            Assert.Equal((1, 0, 0, 0), (dto.LikeCount, dto.DislikeCount, dto.CommentCount, dto.SolutionCount)); // one like from disliker1's account
            Assert.Null(dto.MyVote);
            Assert.True(dto.IsBookmarked);
        }

        // ---- Today ----------------------------------------------------------------------------------------

        [Fact]
        public async Task Today_ReturnsTheSameNumbersAsGetById_NeverSetsBookmark_AndMyVoteIsPerCaller()
        {
            using var db = NewContext();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var anon = Ok(await NewController(db, OwnScope(), cache: cache).GetTodaysChallenge());
            var detail = Ok(await NewController(db, OwnScope()).GetById(anon.Id));
            Assert.Equal((detail.LikeCount, detail.DislikeCount, detail.CommentCount, detail.SolutionCount),
                         (anon.LikeCount, anon.DislikeCount, anon.CommentCount, anon.SolutionCount));
            Assert.Null(anon.MyVote);
            Assert.False(anon.IsBookmarked);

            // pin the pick to _busy via the cache so the per-caller part is deterministic
            var istToday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5).AddMinutes(30));
            cache.Set(TodayCacheKey(OwnScope(), istToday), _busy.Id, TimeSpan.FromMinutes(5));

            var liker = Ok(await NewController(db, OwnScope(), _liker1, cache).GetTodaysChallenge());
            Assert.Equal(_busy.Id, liker.Id);
            Assert.Equal((3, 2, 2, 1), (liker.LikeCount, liker.DislikeCount, liker.CommentCount, liker.SolutionCount));
            Assert.Equal(true, liker.MyVote);
            Assert.False(liker.IsBookmarked); // Today has never reported bookmarks, even for a bookmarker

            var disliker = Ok(await NewController(db, OwnScope(), _disliker1, cache).GetTodaysChallenge());
            Assert.Equal(false, disliker.MyVote);
        }

        private static string TodayCacheKey(MyExamScope scope, DateOnly istToday)
        {
            var scopeKey = scope.IsScoped
                ? "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(string.Join(",", scope.ExamIds.OrderBy(id => id)))))[..16]
                : string.Empty;
            return $"qb-todays-challenge-{istToday:yyyy-MM-dd}{scopeKey}";
        }

        // ---- the gate around Today's once-per-day pick ----------------------------------------------------
        //
        // The in-memory provider finishes queries synchronously, so a plain "run N requests at once" test could
        // not tell a gated pick from an ungated one. Instead the test HOLDS the controller's per-scope gate (the
        // static dictionary the catalog caches also use; read by reflection -- this test breaks, loudly, if that
        // field is renamed) and observes what a request does while it is held.

        private static SemaphoreSlim TodayGate(MyExamScope scope)
        {
            var scopeKey = "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(string.Join(",", scope.ExamIds.OrderBy(id => id)))))[..16];
            var field = typeof(QuestionBankController).GetField("_catalogCacheLocks", BindingFlags.NonPublic | BindingFlags.Static)!;
            var locks = (ConcurrentDictionary<string, SemaphoreSlim>)field.GetValue(null)!;
            return locks.GetOrAdd("qb-todays-challenge-gate" + scopeKey, _ => new SemaphoreSlim(1, 1));
        }

        // A scope no other test uses, so holding its gate cannot disturb tests that run in parallel.
        private MyExamScope UniqueScope() => new(true, new[] { Guid.NewGuid() }, new[] { "unique" });

        [Fact]
        public async Task Today_ACacheMiss_WaitsForTheGate_AndThenUsesWhatTheHolderCached()
        {
            using var db = NewContext();
            // make the unique scope see both questions, so the pick has two candidates
            var scope = new MyExamScope(true, new[] { _exam.Id, Guid.NewGuid() }, new[] { _exam.Name, "other" });
            var gate = TodayGate(scope);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var istToday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5).AddMinutes(30));

            await gate.WaitAsync();   // pretend another request is computing today's pick
            Task<ActionResult<QuestionBankQuestionResponseDto>> waiting;
            try
            {
                waiting = NewController(db, scope, cache: cache).GetTodaysChallenge();
                await Task.Delay(250);
                Assert.False(waiting.IsCompleted, "a cache miss must queue behind the gate instead of recomputing");

                // the holder finishes and caches its pick (the question the computation would NOT choose)
                var ordered = new[] { _busy.Id, _quiet.Id }.OrderBy(x => x).ToArray();
                var computed = ordered[istToday.DayNumber % 2];
                var holderPick = ordered.First(id => id != computed);
                cache.Set(TodayCacheKey(scope, istToday), holderPick, TimeSpan.FromMinutes(5));
                gate.Release();

                var dto = Ok(await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal(holderPick, dto.Id);   // re-checked the cache after the wait: no second computation
            }
            finally
            {
                if (gate.CurrentCount == 0) gate.Release();
            }
        }

        [Fact]
        public async Task Today_AWaiterThatIsCancelled_LeavesWithoutTouchingTheGate()
        {
            using var db = NewContext();
            var scope = UniqueScope();
            var gate = TodayGate(scope);
            using var cts = new CancellationTokenSource();

            await gate.WaitAsync();
            try
            {
                var waiting = NewController(db, scope).GetTodaysChallenge(cts.Token);
                await Task.Delay(100);
                Assert.False(waiting.IsCompleted);
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
                Assert.Equal(0, gate.CurrentCount);   // still held by the test: the cancelled waiter did not Release a gate it never took
            }
            finally
            {
                gate.Release();
            }
            Assert.Equal(1, gate.CurrentCount);       // and it is free again afterwards
        }

        [Fact]
        public async Task Today_NothingVisibleInScope_StillAnswers404_AndReleasesTheGate()
        {
            using var db = NewContext();
            var scope = UniqueScope();   // an exam with no questions
            var gate = TodayGate(scope);
            var r = await NewController(db, scope).GetTodaysChallenge();
            Assert.IsType<NotFoundObjectResult>(r.Result);
            Assert.Equal(1, gate.CurrentCount);
            // and nothing was cached, so adding a question later is picked up on the next call
            var next = await NewController(db, scope).GetTodaysChallenge();
            Assert.IsType<NotFoundObjectResult>(next.Result);
        }

        // ---- vote endpoint ---------------------------------------------------------------------------------

        private QuestionVotesController NewVotes(ScoramDbContext db, Guid userId) =>
            new(db) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(userId) } } };

        private static QuestionVoteResponseDto Vote(ActionResult<QuestionVoteResponseDto> r) =>
            (QuestionVoteResponseDto)((OkObjectResult)r.Result!).Value!;

        [Fact]
        public async Task VoteEndpoint_Counts_AfterLike_Retract_Dislike_Switch_MatchHandComputedNumbers()
        {
            using var db = NewContext();
            var me = Guid.NewGuid();
            var c = NewVotes(db, me);
            var like = new QuestionVoteRequestDto { IsLike = true };
            var dislike = new QuestionVoteRequestDto { IsLike = false };

            // _busy starts at 3 likes / 2 dislikes
            var r1 = Vote(await c.VoteOnQuestionBankQuestion(_busy.Id, like));
            Assert.Equal((4, 2, (bool?)true), (r1.LikeCount, r1.DislikeCount, r1.MyVote));
            var r2 = Vote(await c.VoteOnQuestionBankQuestion(_busy.Id, like));       // same click again retracts
            Assert.Equal((3, 2, (bool?)null), (r2.LikeCount, r2.DislikeCount, r2.MyVote));
            var r3 = Vote(await c.VoteOnQuestionBankQuestion(_busy.Id, dislike));
            Assert.Equal((3, 3, (bool?)false), (r3.LikeCount, r3.DislikeCount, r3.MyVote));
            var r4 = Vote(await c.VoteOnQuestionBankQuestion(_busy.Id, like));       // switch
            Assert.Equal((4, 2, (bool?)true), (r4.LikeCount, r4.DislikeCount, r4.MyVote));
        }

        [Fact]
        public async Task VoteEndpoint_OnAQuestionWithNoVotes_GoesFromZeroToOneAndBack()
        {
            using var db = NewContext();
            // a fresh question with no votes at all (the grouped query returns NO rows here, which must read as 0/0)
            var fresh = new QuestionBankQuestion { QuestionText = "fresh", SubjectId = _subject.Id, TopicId = _topic.Id };
            db.QuestionBankQuestions.Add(fresh);
            await db.SaveChangesAsync();
            var c = NewVotes(db, Guid.NewGuid());
            var like = new QuestionVoteRequestDto { IsLike = true };

            var up = Vote(await c.VoteOnQuestionBankQuestion(fresh.Id, like));
            Assert.Equal((1, 0, (bool?)true), (up.LikeCount, up.DislikeCount, up.MyVote));
            var down = Vote(await c.VoteOnQuestionBankQuestion(fresh.Id, like));
            Assert.Equal((0, 0, (bool?)null), (down.LikeCount, down.DislikeCount, down.MyVote));
        }

        [Fact]
        public async Task VoteEndpoint_LegacyQuestion_CountsStayWithinThatQuestion()
        {
            using var db = NewContext();
            var a = new Question { };
            var b = new Question { };
            db.Questions.AddRange(a, b);
            db.QuestionVotes.Add(new QuestionVote { QuestionId = a.Id, UserId = Guid.NewGuid(), IsLike = true });
            db.QuestionVotes.Add(new QuestionVote { QuestionId = b.Id, UserId = Guid.NewGuid(), IsLike = false });
            db.QuestionVotes.Add(new QuestionVote { QuestionBankQuestionId = _busy.Id, UserId = Guid.NewGuid(), IsLike = false });
            await db.SaveChangesAsync();

            var r = Vote(await NewVotes(db, Guid.NewGuid()).VoteOnQuestion(a.Id, new QuestionVoteRequestDto { IsLike = false }));
            Assert.Equal((1, 1, (bool?)false), (r.LikeCount, r.DislikeCount, r.MyVote));
        }

        // ---- legacy QuestionsController.GetById ------------------------------------------------------------

        private QuestionsController NewLegacy(ScoramDbContext db, Guid? userId)
        {
            return new QuestionsController(
                db, Mock.Of<IAdminPermissionService>(), Mock.Of<IFileStorageService>(), Mock.Of<IInstantSearchService>(),
                Mock.Of<IFallbackSearchService>(), new MemoryCache(new MemoryCacheOptions()), Mock.Of<ILogger<QuestionsController>>(),
                Mock.Of<IAuditLogService>(), Mock.Of<IQuestionBankMirrorService>(), Mock.Of<IMyExamScopeService>())
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(userId) } }
            };
        }

        [Fact]
        public async Task LegacyGetById_CountsMyVoteAndBookmark_ArePerCaller_AndZerosWhenQuiet()
        {
            using var db = NewContext();
            var voter = Guid.NewGuid();
            var other = Guid.NewGuid();
            var busy = new Question { };
            var quiet = new Question { };
            db.Questions.AddRange(busy, quiet);
            db.QuestionVotes.Add(new QuestionVote { QuestionId = busy.Id, UserId = voter, IsLike = true });
            db.QuestionVotes.Add(new QuestionVote { QuestionId = busy.Id, UserId = Guid.NewGuid(), IsLike = true });
            db.QuestionVotes.Add(new QuestionVote { QuestionId = busy.Id, UserId = other, IsLike = false });
            db.QuestionVotes.Add(new QuestionVote { QuestionBankQuestionId = _busy.Id, UserId = Guid.NewGuid(), IsLike = false }); // other table: must not leak
            db.Bookmarks.Add(new Bookmark { UserId = voter, QuestionId = busy.Id });
            await db.SaveChangesAsync();

            static QuestionDetailDto Dto(ActionResult<QuestionDetailDto> r) => (QuestionDetailDto)((OkObjectResult)r.Result!).Value!;

            var anon = Dto(await NewLegacy(db, null).GetById(busy.Id));
            Assert.Equal((2, 1), (anon.LikeCount, anon.DislikeCount));
            Assert.Null(anon.MyVote);
            Assert.False(anon.IsBookmarked);

            var v = Dto(await NewLegacy(db, voter).GetById(busy.Id));
            Assert.Equal((2, 1, (bool?)true, true), (v.LikeCount, v.DislikeCount, v.MyVote, v.IsBookmarked));

            var o = Dto(await NewLegacy(db, other).GetById(busy.Id));
            Assert.Equal(((bool?)false, false), (o.MyVote, o.IsBookmarked));

            var q = Dto(await NewLegacy(db, voter).GetById(quiet.Id));
            Assert.Equal((0, 0, (bool?)null, false), (q.LikeCount, q.DislikeCount, q.MyVote, q.IsBookmarked));

            Assert.IsType<NotFoundResult>((await NewLegacy(db, voter).GetById(Guid.NewGuid())).Result);
        }
    }
}
