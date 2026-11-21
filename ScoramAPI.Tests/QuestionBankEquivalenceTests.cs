using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
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
    // PROOF (as far as the in-memory provider can give it) that the performance changes in
    // QuestionBankController did not change what the endpoints return.
    //
    //   * GetSubjects (global branch), GetTopics (global branch) and GetExams were rewritten for speed.
    //     Each Ref* method below is the PREVIOUS implementation copied verbatim; every scope scenario runs
    //     the controller and the reference against the same seeded database and requires identical JSON.
    //   * Search / GetById vote counts (now one grouped query) are pinned to explicit expected numbers.
    //   * VisibleQuestions() itself was NOT changed; the visibility tests pin its rules (all eight cases
    //     from the task) through the public endpoints so a future edit that weakens it fails here.
    //   * ExamTaxonomyService student COUNT helpers (GetStudentSubjectsAsync/GetStudentTopicsAsync) are pinned by
    //     the test below: precedence / fallback / unassigned / multi-year / inactive handling, so any later
    //     optimization has a safety net. (Round 5 changed LoadTaxonomyAsync's topic query to an IN list and
    //     rewrote FilterByExamSubjects/FilterByExamTopics; the latter are covered by
    //     ExamTaxonomyFilterEquivalenceTests, and this test must keep passing unchanged.)
    //
    // Limits (honest): the in-memory provider is not SQL Server. It proves the logic and the numbers; it
    // cannot prove the generated SQL, its plan, or any timing. Use ToQueryString()/Query Store on a copy of
    // the production data for that.
    public class QuestionBankEquivalenceTests : IDisposable
    {
        private readonly string _dbName = Guid.NewGuid().ToString();
        private readonly DateTime _t0 = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        private int _tick;

        // exams
        private readonly Exam _examA = new() { Name = "Exam A" };
        private readonly Exam _examB = new() { Name = "Exam B" };
        private readonly Exam _examC = new() { Name = "Exam C" };
        private readonly Exam _examD = new() { Name = "Exam D" }; // only ever tagged on a hidden question
        // global taxonomy
        private readonly QuestionBankSubject _s1 = new() { Name = "Subject 1" };
        private readonly QuestionBankSubject _s2 = new() { Name = "Subject 2" };
        private readonly QuestionBankSubject _s3Inactive = new() { Name = "Subject 3 (inactive)", IsActive = false };
        private readonly QuestionBankSubject _s4Empty = new() { Name = "Subject 4 (nothing visible)" };
        private readonly QuestionBankTopic _t1, _t2, _t3, _t4;
        // papers
        private readonly Paper _draft, _pending, _published;
        // questions
        private QuestionBankQuestion _direct = null!, _inactive = null!, _testOnly = null!, _draftMirror = null!,
            _pendingMirror = null!, _publishedMirror = null!, _draftAndPublished = null!, _twoDrafts = null!,
            _nullPaperSource = null!, _unrelatedExam = null!, _twoPublished = null!;

        public QuestionBankEquivalenceTests()
        {
            _t1 = new() { Name = "Topic 1", SubjectId = _s1.Id };
            _t2 = new() { Name = "Topic 2", SubjectId = _s1.Id };
            _t3 = new() { Name = "Topic 3", SubjectId = _s2.Id };
            _t4 = new() { Name = "Topic 4", SubjectId = _s4Empty.Id };
            _draft = new() { ExamId = _examA.Id, Year = 2020, Status = PaperStatus.Draft };
            _pending = new() { ExamId = _examA.Id, Year = 2020, Status = PaperStatus.PendingReview };
            _published = new() { ExamId = _examA.Id, Year = 2020, Status = PaperStatus.Published };
            Seed();
        }

        public void Dispose()
        {
            using var db = NewContext();
            db.Database.EnsureDeleted();
        }

        private ScoramDbContext NewContext() => new(
            new DbContextOptionsBuilder<ScoramDbContext>().UseInMemoryDatabase(_dbName).Options);

        // ---- fixtures -------------------------------------------------------------------------------

        private QuestionBankQuestion Qbq(ScoramDbContext db, string text, QuestionBankSubject s, QuestionBankTopic t,
            bool active = true, bool testOnly = false)
        {
            var q = new QuestionBankQuestion
            {
                QuestionText = text, SubjectId = s.Id, TopicId = t.Id, IsActive = active, IsTestOnly = testOnly,
                CreatedAt = _t0.AddMinutes(++_tick) // distinct, so the default newest-first order is deterministic
            };
            db.QuestionBankQuestions.Add(q);
            return q;
        }

        private static void Map(ScoramDbContext db, QuestionBankQuestion q, Exam e, int year) =>
            db.QuestionBankExamMappings.Add(new QuestionBankExamMapping { QuestionBankQuestionId = q.Id, ExamId = e.Id, Year = year });

        // One PYP source question mirrored to `q`. paper == null models Question.PaperId being NULL (legacy rows).
        private static void Source(ScoramDbContext db, QuestionBankQuestion q, Paper? paper) =>
            db.Questions.Add(new Question { PaperId = paper?.Id, MirroredToQuestionBankQuestionId = q.Id });

        private void Seed()
        {
            using var db = NewContext();
            db.AddRange(_examA, _examB, _examC, _examD, _s1, _s2, _s3Inactive, _s4Empty, _t1, _t2, _t3, _t4,
                _draft, _pending, _published);

            // VISIBLE
            _direct = Qbq(db, "direct", _s1, _t1);                  // active, no mirror source; two years of ONE exam
            Map(db, _direct, _examA, 2020); Map(db, _direct, _examA, 2021);
            _publishedMirror = Qbq(db, "published mirror", _s2, _t3);
            Source(db, _publishedMirror, _published);
            Map(db, _publishedMirror, _examA, 2018); Map(db, _publishedMirror, _examB, 2018);
            _draftAndPublished = Qbq(db, "draft+published sources", _s2, _t3);
            Source(db, _draftAndPublished, _draft); Source(db, _draftAndPublished, _published);
            Map(db, _draftAndPublished, _examB, 2022);
            _unrelatedExam = Qbq(db, "direct, other exam", _s2, _t3);
            Map(db, _unrelatedExam, _examC, 2017);
            _twoPublished = Qbq(db, "two published sources", _s1, _t2);
            Source(db, _twoPublished, _published); Source(db, _twoPublished, _published);
            Map(db, _twoPublished, _examA, 2023);

            // HIDDEN
            _inactive = Qbq(db, "inactive", _s4Empty, _t4, active: false);
            Map(db, _inactive, _examA, 2020);
            _testOnly = Qbq(db, "test only", _s1, _t1, testOnly: true);
            Map(db, _testOnly, _examA, 2020);
            _draftMirror = Qbq(db, "draft mirror", _s1, _t2);
            Source(db, _draftMirror, _draft);
            Map(db, _draftMirror, _examD, 2019);
            _pendingMirror = Qbq(db, "pending mirror", _s1, _t2);
            Source(db, _pendingMirror, _pending);
            Map(db, _pendingMirror, _examA, 2019);
            _twoDrafts = Qbq(db, "draft+pending sources", _s2, _t3);
            Source(db, _twoDrafts, _draft); Source(db, _twoDrafts, _pending);
            Map(db, _twoDrafts, _examB, 2022);
            _nullPaperSource = Qbq(db, "source without a paper", _s2, _t3);
            Source(db, _nullPaperSource, null);
            Map(db, _nullPaperSource, _examB, 2022);

            // votes + comments on the first visible question (3 likes, 1 dislike, 2 comments); none elsewhere
            var voters = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
            for (var i = 0; i < 4; i++)
                db.QuestionVotes.Add(new QuestionVote { QuestionBankQuestionId = _direct.Id, UserId = voters[i], IsLike = i < 3 });
            // a vote on a DIFFERENT question must never leak into _direct's counts
            db.QuestionVotes.Add(new QuestionVote { QuestionBankQuestionId = _publishedMirror.Id, UserId = voters[0], IsLike = false });
            db.QuestionComments.Add(new QuestionComment { QuestionBankQuestionId = _direct.Id, CommentText = "c1" });
            db.QuestionComments.Add(new QuestionComment { QuestionBankQuestionId = _direct.Id, CommentText = "c2" });

            db.SaveChanges();
        }

        // ---- controller plumbing --------------------------------------------------------------------

        private MyExamScope ScopeFor(string key) => key switch
        {
            "unscoped" => MyExamScope.Unscoped,
            "empty" => new MyExamScope(true, Array.Empty<Guid>(), Array.Empty<string>()),
            "A" => new MyExamScope(true, new[] { _examA.Id }, new[] { _examA.Name }),
            "B" => new MyExamScope(true, new[] { _examB.Id }, new[] { _examB.Name }),
            "AB" => new MyExamScope(true, new[] { _examA.Id, _examB.Id }, new[] { _examA.Name, _examB.Name }),
            "D" => new MyExamScope(true, new[] { _examD.Id }, new[] { _examD.Name }),
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };

        private static (List<Guid> Allowed, bool Scoped) Split(MyExamScope s) => (s.ExamIds.ToList(), s.IsScoped);

        // cache: shared across NewController calls only when a test passes one explicitly (the cache
        // tests below) -- every pre-existing call site omits it and keeps getting its own fresh,
        // empty MemoryCache exactly as before, so this parameter changes nothing for those tests.
        //
        // API-06 additions (both optional, both default to the old behaviour, so no pre-existing call
        // changes): scopeHonorsCancellation makes the mocked scope service throw for an already-cancelled
        // token (like the real one does through ToListAsync(ct)) -- that deterministically proves the
        // controller PASSES its token down, without relying on the in-memory provider to observe it;
        // taxonomy lets a test substitute IExamTaxonomyService (default: the real service as before).
        private QuestionBankController NewController(
            ScoramDbContext db, MyExamScope scope, Guid? userId = null, IMemoryCache? cache = null,
            bool scopeHonorsCancellation = false, IExamTaxonomyService? taxonomy = null,
            ILogger<QuestionBankController>? logger = null)
        {
            var scopeSvc = new Mock<IMyExamScopeService>();
            if (scopeHonorsCancellation)
                scopeSvc.Setup(s => s.GetScopeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
                    .Returns<ClaimsPrincipal, CancellationToken>((_, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        return Task.FromResult(scope);
                    });
            else
                scopeSvc.Setup(s => s.GetScopeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>())).ReturnsAsync(scope);
            var identity = userId.HasValue
                ? new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()) }, "test")
                : new ClaimsIdentity();
            return new QuestionBankController(
                db, Mock.Of<IGamificationService>(), logger ?? Mock.Of<ILogger<QuestionBankController>>(), scopeSvc.Object,
                taxonomy ?? new ExamTaxonomyService(db, Mock.Of<ILogger<ExamTaxonomyService>>()),
                cache ?? new MemoryCache(new MemoryCacheOptions()))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } }
            };
        }

        private static T Ok<T>(ActionResult<T> result) => (T)((OkObjectResult)result.Result!).Value!;
        private static string Json(object? o) => JsonSerializer.Serialize(o);

        // ---- the previous implementations, verbatim (references) -------------------------------------

        private static async Task<List<QuestionBankSubjectDto>> RefSubjects(ScoramDbContext db, bool scoped, List<Guid> allowedExamIds)
        {
            var subjects = await db.QuestionBankSubjects
                .Where(s => s.IsActive)
                .OrderBy(s => s.Name)
                .Select(s => new QuestionBankSubjectDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    IsActive = s.IsActive,
                    QuestionCount = s.Questions.Count(q => q.IsActive && !q.IsTestOnly
                        && (!db.Questions.Any(src => src.MirroredToQuestionBankQuestionId == q.Id)
                            || db.Questions.Any(src => src.MirroredToQuestionBankQuestionId == q.Id && src.Paper!.Status == PaperStatus.Published))
                        && (!scoped || q.ExamMappings.Any(m => allowedExamIds.Contains(m.ExamId))))
                })
                .ToListAsync();
            if (scoped) subjects = subjects.Where(s => s.QuestionCount > 0).ToList();
            return subjects;
        }

        private static async Task<List<QuestionBankTopicDto>> RefTopics(ScoramDbContext db, Guid? subjectId, bool scoped, List<Guid> allowedExamIds)
        {
            var q = db.QuestionBankTopics.Where(t => t.IsActive).AsQueryable();
            if (subjectId.HasValue) q = q.Where(t => t.SubjectId == subjectId.Value);
            var topics = await q
                .Include(t => t.Subject)
                .OrderBy(t => t.Name)
                .Select(t => new QuestionBankTopicDto
                {
                    Id = t.Id,
                    SubjectId = t.SubjectId,
                    SubjectName = t.Subject!.Name,
                    Name = t.Name,
                    IsActive = t.IsActive,
                    QuestionCount = t.Questions.Count(q => q.IsActive && !q.IsTestOnly
                        && (!db.Questions.Any(src => src.MirroredToQuestionBankQuestionId == q.Id)
                            || db.Questions.Any(src => src.MirroredToQuestionBankQuestionId == q.Id && src.Paper!.Status == PaperStatus.Published))
                        && (!scoped || q.ExamMappings.Any(m => allowedExamIds.Contains(m.ExamId))))
                })
                .ToListAsync();
            if (scoped) topics = topics.Where(t => t.QuestionCount > 0).ToList();
            return topics;
        }

        private static IQueryable<QuestionBankQuestion> RefVisible(ScoramDbContext db) =>
            db.QuestionBankQuestions.Where(x => x.IsActive && !x.IsTestOnly
                && (!db.Questions.Any(src => src.MirroredToQuestionBankQuestionId == x.Id)
                    || db.Questions.Any(src => src.MirroredToQuestionBankQuestionId == x.Id && src.Paper!.Status == PaperStatus.Published)));

        private static async Task<List<(Guid Id, string Name, string? Logo)>> RefExams(ScoramDbContext db, bool scoped, List<Guid> allowedExamIds)
        {
            var rows = await db.QuestionBankExamMappings
                .Where(m => (!scoped || allowedExamIds.Contains(m.ExamId))
                    && RefVisible(db).Any(v => v.Id == m.QuestionBankQuestionId))
                .Select(m => m.Exam)
                .Where(e => e != null)
                .Distinct()
                .OrderBy(e => e!.Name)
                .Select(e => new { id = e!.Id, name = e.Name, logoUrl = e.LogoUrl })
                .ToListAsync();
            return rows.Select(r => (r.id, r.name, r.logoUrl)).ToList();
        }

        public static IEnumerable<object[]> ScopeKeys() =>
            new[] { "unscoped", "empty", "A", "B", "AB", "D" }.Select(k => new object[] { k });

        // ---- visibility (VisibleQuestions is unchanged -- these pin its rules) -----------------------

        [Fact]
        public async Task Search_ShowsOnlyVisibleQuestions_AllEightVisibilityCases()
        {
            using var db = NewContext();
            var c = NewController(db, MyExamScope.Unscoped);
            var page = Ok(await c.Search(new QuestionBankSearchQuery { PageSize = 100 }));

            var expectedVisible = new[] { _direct, _publishedMirror, _draftAndPublished, _unrelatedExam, _twoPublished };
            Assert.Equal(expectedVisible.Select(q => q.Id).OrderBy(x => x), page.Items.Select(i => i.Id).OrderBy(x => x));
            Assert.Equal(5, page.TotalCount);

            var hidden = new[] { _inactive, _testOnly, _draftMirror, _pendingMirror, _twoDrafts, _nullPaperSource };
            Assert.Empty(page.Items.Select(i => i.Id).Intersect(hidden.Select(q => q.Id)));
        }

        [Fact]
        public async Task GetById_HiddenQuestions_AreNotFound_VisibleOnesAreReturned()
        {
            using var db = NewContext();
            var c = NewController(db, MyExamScope.Unscoped);
            foreach (var hidden in new[] { _inactive, _testOnly, _draftMirror, _pendingMirror, _twoDrafts, _nullPaperSource })
                Assert.IsType<NotFoundObjectResult>((await c.GetById(hidden.Id)).Result);
            foreach (var visible in new[] { _direct, _publishedMirror, _draftAndPublished, _unrelatedExam, _twoPublished })
                Assert.Equal(visible.Id, Ok(await c.GetById(visible.Id)).Id);
        }

        // Unpublishing / publishing is evaluated at query time -- no stale state.
        [Fact]
        public async Task PublishingTheOnlySourcePaper_MakesAHiddenMirrorVisible()
        {
            using (var db = NewContext())
            {
                var p = await db.Papers.SingleAsync(x => x.Id == _pending.Id);
                p.Status = PaperStatus.Published;
                await db.SaveChangesAsync();
            }
            using var db2 = NewContext();
            var c = NewController(db2, MyExamScope.Unscoped);
            Assert.Equal(_pendingMirror.Id, Ok(await c.GetById(_pendingMirror.Id)).Id);
            // _twoDrafts has a Draft AND a (now Published) PendingReview source -> now visible too
            Assert.Equal(_twoDrafts.Id, Ok(await c.GetById(_twoDrafts.Id)).Id);
        }

        // ---- Subjects / Topics / Exams: optimized == previous implementation ------------------------

        [Theory]
        [MemberData(nameof(ScopeKeys))]
        public async Task GetSubjects_Global_MatchesPreviousImplementation(string key)
        {
            var scope = ScopeFor(key);
            var (allowed, scoped) = Split(scope);
            using var db = NewContext();
            var actual = Ok(await NewController(db, scope).GetSubjects());
            var expected = await RefSubjects(db, scoped, allowed);
            Assert.Equal(Json(expected), Json(actual));
        }

        [Theory]
        [InlineData("unscoped", true)] [InlineData("unscoped", false)]
        [InlineData("A", true)] [InlineData("A", false)]
        [InlineData("AB", true)] [InlineData("AB", false)]
        [InlineData("empty", false)] [InlineData("D", false)]
        public async Task GetTopics_Global_MatchesPreviousImplementation(string key, bool withSubjectFilter)
        {
            var scope = ScopeFor(key);
            var (allowed, scoped) = Split(scope);
            using var db = NewContext();
            Guid? subjectId = withSubjectFilter ? _s1.Id : null;
            var actual = Ok(await NewController(db, scope).GetTopics(subjectId));
            var expected = await RefTopics(db, subjectId, scoped, allowed);
            Assert.Equal(Json(expected), Json(actual));
        }

        [Theory]
        [MemberData(nameof(ScopeKeys))]
        public async Task GetExams_MatchesPreviousImplementation(string key)
        {
            var scope = ScopeFor(key);
            var (allowed, scoped) = Split(scope);
            using var db = NewContext();
            var actual = Ok(await NewController(db, scope).GetExams());
            var expected = await RefExams(db, scoped, allowed);
            Assert.Equal(
                Json(expected.Select(e => new { id = e.Id, name = e.Name, logoUrl = e.Logo })),
                Json(actual));
        }

        // Explicit numbers, so the references above can't be wrong in the same way as the controller.
        [Fact]
        public async Task GetSubjects_Global_ExplicitCounts()
        {
            using var db = NewContext();
            var all = Ok(await NewController(db, MyExamScope.Unscoped).GetSubjects());
            Assert.Equal(new[] { ("Subject 1", 2), ("Subject 2", 3), ("Subject 4 (nothing visible)", 0) },
                all.Select(s => (s.Name, s.QuestionCount)).ToArray());   // inactive subject never listed; empty one listed with 0

            var a = Ok(await NewController(db, ScopeFor("A")).GetSubjects());
            Assert.Equal(new[] { ("Subject 1", 2), ("Subject 2", 1) }, a.Select(s => (s.Name, s.QuestionCount)).ToArray());

            Assert.Empty(Ok(await NewController(db, ScopeFor("empty")).GetSubjects())); // nothing selected -> nothing, never "all"
        }

        [Fact]
        public async Task GetTopics_Global_ExplicitCounts()
        {
            using var db = NewContext();
            var all = Ok(await NewController(db, MyExamScope.Unscoped).GetTopics(null));
            Assert.Equal(new[] { ("Topic 1", 1), ("Topic 2", 1), ("Topic 3", 3), ("Topic 4", 0) },
                all.Select(t => (t.Name, t.QuestionCount)).ToArray());

            var s1Only = Ok(await NewController(db, MyExamScope.Unscoped).GetTopics(_s1.Id));
            Assert.Equal(new[] { "Topic 1", "Topic 2" }, s1Only.Select(t => t.Name).ToArray());
        }

        [Fact]
        public async Task GetExams_ExplicitLists()
        {
            using var db = NewContext();
            // the controller returns anonymous objects (internal to its assembly), so read them back as JSON
            var unscoped = JsonSerializer.SerializeToElement(Ok(await NewController(db, MyExamScope.Unscoped).GetExams()));
            Assert.Equal(new[] { "Exam A", "Exam B", "Exam C" },
                unscoped.EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToArray());
            Assert.Single(Ok(await NewController(db, ScopeFor("A")).GetExams()));
            Assert.Empty(Ok(await NewController(db, ScopeFor("D")).GetExams()));      // only a Draft-mirrored question -> no exam offered
            Assert.Empty(Ok(await NewController(db, ScopeFor("empty")).GetExams()));
        }

        [Fact]
        public async Task GetYears_Unchanged_OnlyYearsOfVisibleQuestions()
        {
            using var db = NewContext();
            var years = Ok(await NewController(db, MyExamScope.Unscoped).GetYears());
            Assert.Equal(new[] { 2023, 2022, 2021, 2020, 2018, 2017 }, years); // 2019 (draft/pending only) is excluded
            Assert.Equal(new[] { 2023, 2021, 2020, 2018 }, Ok(await NewController(db, ScopeFor("A")).GetYears()));
        }

        // ---- votes / comments -----------------------------------------------------------------------

        [Fact]
        public async Task GetById_LikeDislikeCommentCounts_AndZeroWhenNoVotes()
        {
            using var db = NewContext();
            var c = NewController(db, MyExamScope.Unscoped);
            var withVotes = Ok(await c.GetById(_direct.Id));
            Assert.Equal((3, 1, 2), (withVotes.LikeCount, withVotes.DislikeCount, withVotes.CommentCount));

            var onlyDislike = Ok(await c.GetById(_publishedMirror.Id));
            Assert.Equal((0, 1, 0), (onlyDislike.LikeCount, onlyDislike.DislikeCount, onlyDislike.CommentCount));

            var none = Ok(await c.GetById(_twoPublished.Id));
            Assert.Equal((0, 0, 0), (none.LikeCount, none.DislikeCount, none.CommentCount));
        }

        [Fact]
        public async Task Search_PerCardCounts_MatchGetById_AndMyVoteIsPerCaller()
        {
            using var db = NewContext();
            var voter = await db.QuestionVotes.Where(v => v.QuestionBankQuestionId == _direct.Id && !v.IsLike).Select(v => v.UserId).SingleAsync();
            var c = NewController(db, MyExamScope.Unscoped, voter);
            var page = Ok(await c.Search(new QuestionBankSearchQuery { PageSize = 100 }));

            foreach (var item in page.Items)
            {
                var detail = Ok(await c.GetById(item.Id));
                Assert.Equal((detail.LikeCount, detail.DislikeCount, detail.CommentCount), (item.LikeCount, item.DislikeCount, item.CommentCount));
            }
            var direct = page.Items.Single(i => i.Id == _direct.Id);
            Assert.Equal((3, 1, 2), (direct.LikeCount, direct.DislikeCount, direct.CommentCount));
            Assert.False(direct.MyVote); // this caller disliked it
        }

        // ---- search paging / response shape / My Exams scope ----------------------------------------

        [Fact]
        public async Task Search_Paging_NewestFirst_AndScopeNeverLeaksOtherExams()
        {
            using var db = NewContext();
            var p1 = Ok(await NewController(db, MyExamScope.Unscoped).Search(new QuestionBankSearchQuery { Page = 1, PageSize = 2 }));
            var p3 = Ok(await NewController(db, MyExamScope.Unscoped).Search(new QuestionBankSearchQuery { Page = 3, PageSize = 2 }));
            Assert.Equal((5, 1, 2, 2), (p1.TotalCount, p1.Page, p1.PageSize, p1.Items.Count));
            Assert.Single(p3.Items);
            // newest first: _twoPublished was created last among the visible ones, then _unrelatedExam
            Assert.Equal(new[] { _twoPublished.Id, _unrelatedExam.Id }, p1.Items.Select(i => i.Id).ToArray());

            // a client asking for an exam outside My Exams gets nothing (scope only narrows)
            var scopedA = NewController(db, ScopeFor("A"));
            var leak = Ok(await scopedA.Search(new QuestionBankSearchQuery { ExamIds = new List<Guid> { _examC.Id } }));
            Assert.Empty(leak.Items);
            var own = Ok(await scopedA.Search(new QuestionBankSearchQuery { PageSize = 100 }));
            Assert.Equal(new[] { _direct.Id, _publishedMirror.Id, _twoPublished.Id }.OrderBy(x => x), own.Items.Select(i => i.Id).OrderBy(x => x));
        }

        [Fact]
        public async Task Search_ResponseDto_KeepsAllFieldsIncludingAskedInOrdering()
        {
            using var db = NewContext();
            var item = Ok(await NewController(db, MyExamScope.Unscoped).Search(new QuestionBankSearchQuery { PageSize = 100 }))
                .Items.Single(i => i.Id == _direct.Id);
            Assert.Equal("Subject 1", item.Subject);
            Assert.Equal("Topic 1", item.Topic);
            Assert.Equal(new[] { 2021, 2020 }, item.AskedIn.Select(a => a.Year).ToArray()); // year descending
            Assert.All(item.AskedIn, a => Assert.Equal("Exam A", a.ExamName));              // Include(...ThenInclude(Exam)) intact
        }

        [Fact]
        public async Task TodaysChallenge_StillPicksOnlyAVisibleInScopeQuestion()
        {
            using var db = NewContext();
            var c = NewController(db, ScopeFor("B"));
            var r = await c.GetTodaysChallenge();
            var dto = Ok(r);
            Assert.Contains(dto.Id, new[] { _publishedMirror.Id, _draftAndPublished.Id }); // the only visible questions mapped to Exam B
            Assert.IsType<NotFoundObjectResult>((await NewController(db, ScopeFor("empty")).GetTodaysChallenge()).Result);
        }

        // ---- ExamTaxonomyService student helpers (unchanged; behaviour pinned) ------------------------

        [Fact]
        public async Task ExamTaxonomy_Student_Counts_Precedence_Fallback_Unassigned_InactiveAndMultiYear()
        {
            using var db = NewContext();

            // Exam A's own taxonomy
            var esAlpha = new ExamSubject { ExamId = _examA.Id, Name = "A-Alpha", BaseSubjectId = _s1.Id };
            var esBeta = new ExamSubject { ExamId = _examA.Id, Name = "B-Beta", BaseSubjectId = _s2.Id };
            var esGamma = new ExamSubject { ExamId = _examA.Id, Name = "C-Gamma" };                       // explicit-only (no base)
            var esDead = new ExamSubject { ExamId = _examA.Id, Name = "D-Inactive", BaseSubjectId = _s4Empty.Id, IsActive = false };
            var etT1 = new ExamTopic { ExamSubjectId = esAlpha.Id, Name = "Alpha-T1", BaseTopicId = _t1.Id };
            var etT2 = new ExamTopic { ExamSubjectId = esAlpha.Id, Name = "Alpha-T2", BaseTopicId = _t2.Id };
            var etT3 = new ExamTopic { ExamSubjectId = esBeta.Id, Name = "Beta-T3", BaseTopicId = _t3.Id };
            var etG = new ExamTopic { ExamSubjectId = esGamma.Id, Name = "Gamma-Only" };
            var etOff = new ExamTopic { ExamSubjectId = esAlpha.Id, Name = "Alpha-Off", IsActive = false };
            db.AddRange(esAlpha, esBeta, esGamma, esDead, etT1, etT2, etT3, etG, etOff);

            // _direct: EXPLICIT classification overrides its global S1/T1 (-> Gamma). Mapped to Exam A in TWO years: counted once.
            db.QuestionBankExamClassifications.Add(new QuestionBankExamClassification
            { QuestionBankQuestionId = _direct.Id, ExamId = _examA.Id, ExamSubjectId = esGamma.Id, ExamTopicId = etG.Id });
            // _publishedMirror: no row -> BaseSubjectId/BaseTopicId fallback (S2/T3 -> Beta / Beta-T3)
            // _twoPublished: no row -> fallback (S1/T2 -> Alpha / Alpha-T2)
            // _pendingMirror (hidden) has a row too, but must not be counted by the student view
            db.QuestionBankExamClassifications.Add(new QuestionBankExamClassification
            { QuestionBankQuestionId = _pendingMirror.Id, ExamId = _examA.Id, ExamSubjectId = esAlpha.Id, ExamTopicId = etT2.Id });
            await db.SaveChangesAsync();

            var svc = new ExamTaxonomyService(db, Mock.Of<ILogger<ExamTaxonomyService>>());
            var visible = RefVisible(db);

            var subjects = await svc.GetStudentSubjectsAsync(_examA.Id, visible);
            Assert.Equal(new[] { ("A-Alpha", 1), ("B-Beta", 1), ("C-Gamma", 1) },
                subjects.Select(s => (s.Name, s.QuestionCount)).ToArray());          // inactive subject not listed; sorted by name

            var topics = await svc.GetStudentTopicsAsync(_examA.Id, null, visible);
            Assert.Equal(new[] { ("Alpha-T1", 0), ("Alpha-T2", 1), ("Beta-T3", 1), ("Gamma-Only", 1) },
                topics.Select(t => (t.Name, t.QuestionCount)).ToArray());            // inactive topic not listed; _direct no longer under Alpha-T1

            var onlyGamma = await svc.GetStudentTopicsAsync(_examA.Id, esGamma.Id, visible);
            Assert.Equal(new[] { "Gamma-Only" }, onlyGamma.Select(t => t.Name).ToArray());

            // The sum of subject counts + unassigned == visible questions of the exam (nothing lost or doubled)
            Assert.Equal(3, subjects.Sum(s => s.QuestionCount));
            Assert.Equal(3, await visible.CountAsync(q => db.QuestionBankExamMappings.Any(m => m.QuestionBankQuestionId == q.Id && m.ExamId == _examA.Id)));
        }

        // ---- API-01: catalog caching (performance round 1) -------------------------------------------
        //
        // The equivalence tests above already prove GetSubjects/GetTopics/GetExams/GetYears compute the
        // right DATA. These three prove the CACHE wrapped around that computation (GetOrAddCatalogCacheAsync
        // + ScopeCacheKeySuffix, shared by all four endpoints through the controller's one _cache field and
        // one static per-key lock dictionary) behaves correctly: a hit actually serves the cached value
        // instead of recomputing, and no two cache keys -- different scopes, or different endpoints -- ever
        // collide. Note: NewController's optional `cache` parameter is new and defaults to a fresh, empty
        // MemoryCache exactly like before, so it changes nothing for every pre-existing test above that
        // omits it; only the three tests below pass one explicitly, to deliberately share it across calls.

        [Fact]
        public async Task GetSubjects_SecondCallWithinTtl_ServesCachedResult_NotFreshData()
        {
            using var db = NewContext();
            var cache = new MemoryCache(new MemoryCacheOptions());

            var first = Json(Ok(await NewController(db, MyExamScope.Unscoped, cache: cache).GetSubjects()));

            // Change the underlying data directly (bypassing the controller) -- an un-cached compute
            // would now see this new, active, visible subject.
            var newSubject = new QuestionBankSubject { Name = "Subject 5 (added after first call)" };
            var newTopic = new QuestionBankTopic { Name = "Topic 5", SubjectId = newSubject.Id };
            db.AddRange(newSubject, newTopic);
            db.QuestionBankQuestions.Add(new QuestionBankQuestion
            {
                QuestionText = "added after first call", SubjectId = newSubject.Id, TopicId = newTopic.Id,
                IsActive = true, CreatedAt = _t0.AddDays(1)
            });
            await db.SaveChangesAsync();

            // Same shared cache, same key, well within the 3-minute TTL -> still the ORIGINAL payload.
            // That's a cache hit, not a coincidence: the control call right below, using its own fresh
            // (unshared) cache against the very same now-changed database, proves the underlying query
            // itself does reflect the new subject when nothing is cached.
            var second = Json(Ok(await NewController(db, MyExamScope.Unscoped, cache: cache).GetSubjects()));
            Assert.Equal(first, second);

            var uncached = Json(Ok(await NewController(db, MyExamScope.Unscoped).GetSubjects()));
            Assert.NotEqual(first, uncached);
        }

        [Fact]
        public async Task GetSubjects_DifferentScopes_ShareOneCache_WithoutCollision()
        {
            using var db = NewContext();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var keys = new[] { "unscoped", "A", "B", "AB", "empty", "D" };

            async Task AssertMatchesReference(string key)
            {
                var scope = ScopeFor(key);
                var (allowed, scoped) = Split(scope);
                var actual = Ok(await NewController(db, scope, cache: cache).GetSubjects());
                var expected = await RefSubjects(db, scoped, allowed);
                Assert.Equal(Json(expected), Json(actual));
            }

            // Pass 1: every key is a cache MISS (fresh compute, then stored).
            foreach (var key in keys) await AssertMatchesReference(key);

            // Pass 2, reverse order: every key is now a cache HIT -- still correct, proving no two
            // scopes' entries (all stored behind the one shared cache and the one shared, static
            // per-key lock dictionary) got mixed up with each other.
            foreach (var key in keys.Reverse()) await AssertMatchesReference(key);
        }

        [Fact]
        public async Task CatalogEndpoints_ShareOneCache_ButNeverCrossEndpointCollide()
        {
            using var db = NewContext();
            var cache = new MemoryCache(new MemoryCacheOptions());

            foreach (var key in new[] { "unscoped", "A", "empty" })
            {
                var scope = ScopeFor(key);
                var (allowed, scoped) = Split(scope);
                var c = NewController(db, scope, cache: cache);

                Assert.Equal(Json(await RefSubjects(db, scoped, allowed)), Json(Ok(await c.GetSubjects())));
                Assert.Equal(Json(await RefTopics(db, null, scoped, allowed)), Json(Ok(await c.GetTopics(null))));

                var expectedExams = await RefExams(db, scoped, allowed);
                Assert.Equal(
                    Json(expectedExams.Select(e => new { id = e.Id, name = e.Name, logoUrl = e.Logo })),
                    Json(Ok(await c.GetExams())));

                // GetYears has no Ref* implementation above (its own logic wasn't touched, only wrapped
                // in the cache); pin it against an un-cached call instead, which
                // GetYears_Unchanged_OnlyYearsOfVisibleQuestions already trusts as correct.
                var uncachedYears = Ok(await NewController(db, scope).GetYears());
                Assert.Equal(uncachedYears, Ok(await c.GetYears()));
            }
        }

        // ---- API-06: cancellation ---------------------------------------------------------------------
        //
        // What these prove (in-memory, honest limits): that every Question Bank read endpoint hands its
        // CancellationToken to the services/queries it calls, and that a cancelled request can neither
        // poison the catalog cache nor leave a per-key gate held. They cannot prove that SQL Server really
        // aborts the in-flight command -- that is a manual check on a real database.

        [Fact]
        public async Task CancelledToken_StopsEveryReadEndpoint_BeforeAnyWork()
        {
            using var db = NewContext();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var ct = cts.Token;
            QuestionBankController C() => NewController(db, MyExamScope.Unscoped, scopeHonorsCancellation: true);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => C().Search(new QuestionBankSearchQuery(), ct));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => C().GetSubjects(null, ct));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => C().GetTopics(null, null, ct));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => C().GetExams(ct));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => C().GetYears(ct));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => C().GetTodaysChallenge(ct));
        }

        [Fact]
        public async Task CancelledToken_DoesNotChangeResults_WhenNotCancelled()
        {
            // The token parameter must be a pure addition: with a live token the endpoints return exactly
            // what they return without one (the equivalence tests above already pin those numbers).
            using var db = NewContext();
            using var cts = new CancellationTokenSource();
            var c = NewController(db, MyExamScope.Unscoped, scopeHonorsCancellation: true);

            Assert.Equal(
                Json(Ok(await NewController(db, MyExamScope.Unscoped).Search(new QuestionBankSearchQuery { PageSize = 100 }))),
                Json(Ok(await c.Search(new QuestionBankSearchQuery { PageSize = 100 }, cts.Token))));
            Assert.Equal(
                Json(Ok(await NewController(db, MyExamScope.Unscoped).GetSubjects())),
                Json(Ok(await NewController(db, MyExamScope.Unscoped).GetSubjects(null, cts.Token))));
        }

        [Fact]
        public async Task CancelledWaitForGate_DoesNotPoisonCache_AndLeavesNothingLocked()
        {
            using var db = NewContext();
            var cache = new MemoryCache(new MemoryCacheOptions());

            // Scope service does NOT throw here, so the cancelled token is first observed inside the cache
            // helper's gate.WaitAsync(ct) -- the path this test is about.
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => NewController(db, MyExamScope.Unscoped, cache: cache).GetSubjects(null, cts.Token));

            // Same cache, fresh live token: must compute normally (nothing cached from the cancelled call,
            // gate not held). Guard with a timeout so a leaked lock fails the test instead of hanging it.
            var next = NewController(db, MyExamScope.Unscoped, cache: cache).GetSubjects(null, CancellationToken.None);
            Assert.Same(next, await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(10))));
            var (allowed, scoped) = Split(MyExamScope.Unscoped);
            Assert.Equal(Json(await RefSubjects(db, scoped, allowed)), Json(Ok(await next)));
        }

        [Fact]
        public async Task CancelledFactory_DoesNotPoisonCache_AndReleasesGate()
        {
            using var db = NewContext();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var examId = _examA.Id;

            // Exam-specific branch: the cached factory calls the taxonomy service, which we make throw
            // OperationCanceledException on its first call only (a request aborted mid-computation).
            var calls = 0;
            var taxonomy = new Mock<IExamTaxonomyService>();
            taxonomy.Setup(t => t.IsInitializedAsync(examId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            taxonomy.Setup(t => t.GetStudentSubjectsAsync(
                    examId, It.IsAny<IQueryable<QuestionBankQuestion>>(), It.IsAny<CancellationToken>()))
                .Returns<Guid, IQueryable<QuestionBankQuestion>, CancellationToken>((_, _, _) =>
                    ++calls == 1
                        ? Task.FromException<List<ExamSubjectFilterDto>>(new OperationCanceledException())
                        : Task.FromResult(new List<ExamSubjectFilterDto>()));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => NewController(db, MyExamScope.Unscoped, cache: cache, taxonomy: taxonomy.Object)
                    .GetSubjects(examId, CancellationToken.None));

            var second = NewController(db, MyExamScope.Unscoped, cache: cache, taxonomy: taxonomy.Object)
                .GetSubjects(examId, CancellationToken.None);
            Assert.Same(second, await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(10)))); // gate was released
            Assert.Empty(Ok(await second));
            Assert.Equal(2, calls); // the failed first computation was not cached -> it really recomputed
        }
    }
}
