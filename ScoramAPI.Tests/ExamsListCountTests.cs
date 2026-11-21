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
    // API-07: GET /api/exams (List) and GET /api/admin/exams (AdminList) now compute each exam's
    // Question Bank count with ONE server-side grouped query instead of loading the whole bank and
    // grouping in C#. These tests pin that the NUMBERS did not change:
    //   * explicit expected counts for a seeded database covering every rule, and
    //   * the previous in-memory algorithm, copied verbatim as RefBankCounts, run against the same
    //     database and required to agree with what the controller returns for every exam.
    //
    // Rule (unchanged): per exam, DISTINCT bank questions that are IsActive, not IsTestOnly, tagged to
    // that exam in any year, and NOT the target of any Question.MirroredToQuestionBankQuestionId --
    // every mirror is excluded whatever its paper's status. The PYP half (legacy Question.ExamId plus
    // questions on the exam's papers) is untouched and added on top.
    //
    // Limits (honest): the in-memory provider proves the numbers, NOT that EF translates the new query to
    // SQL Server, nor its speed. The first run against SQL Server (see the report's checklist) must
    // confirm both.
    public class ExamsListCountTests : IDisposable
    {
        private readonly string _dbName = Guid.NewGuid().ToString();

        private readonly Exam _examA = new() { Name = "Exam A" };
        private readonly Exam _examB = new() { Name = "Exam B" };
        private readonly Exam _examC = new() { Name = "Exam C (no content)" };
        private readonly Exam _examD = new() { Name = "Exam D (PYP + bank)" };

        public ExamsListCountTests() => Seed();

        public void Dispose()
        {
            using var db = NewContext();
            db.Database.EnsureDeleted();
        }

        private ScoramDbContext NewContext() => new(
            new DbContextOptionsBuilder<ScoramDbContext>().UseInMemoryDatabase(_dbName).Options);

        private static ExamsController NewController(ScoramDbContext db) => new(
            db, Mock.Of<IFileStorageService>(), Mock.Of<IAdminPermissionService>(), Mock.Of<IAuditLogService>());

        private static QuestionBankQuestion Qbq(ScoramDbContext db, string text, bool active = true, bool testOnly = false)
        {
            var q = new QuestionBankQuestion
            {
                QuestionText = text, SubjectId = Guid.NewGuid(), TopicId = Guid.NewGuid(),
                IsActive = active, IsTestOnly = testOnly
            };
            db.QuestionBankQuestions.Add(q);
            return q;
        }

        private static void Map(ScoramDbContext db, QuestionBankQuestion q, Exam e, int year) =>
            db.QuestionBankExamMappings.Add(new QuestionBankExamMapping { QuestionBankQuestionId = q.Id, ExamId = e.Id, Year = year });

        private static void Source(ScoramDbContext db, QuestionBankQuestion q, Paper? paper) =>
            db.Questions.Add(new Question { PaperId = paper?.Id, MirroredToQuestionBankQuestionId = q.Id });

        // Expected BANK counts (what the aggregate must return):
        //   A = 2  (direct: two years of A counts once; shared: tagged to A and B)
        //   B = 1  (shared)
        //   C = 0  (no content at all)
        //   D = 1  (direct bank question)  -- plus its PYP question below
        // Hidden from the count: inactive, test-only, and mirrors of Draft / Pending / Published papers.
        private void Seed()
        {
            using var db = NewContext();
            db.AddRange(_examA, _examB, _examC, _examD);

            var draft = new Paper { ExamId = _examA.Id, Year = 2020, Status = PaperStatus.Draft };
            var pending = new Paper { ExamId = _examA.Id, Year = 2020, Status = PaperStatus.PendingReview };
            var published = new Paper { ExamId = _examA.Id, Year = 2020, Status = PaperStatus.Published };
            db.AddRange(draft, pending, published);

            var direct = Qbq(db, "direct");
            Map(db, direct, _examA, 2020); Map(db, direct, _examA, 2021);   // same exam, two years -> once

            var shared = Qbq(db, "shared across exams");
            Map(db, shared, _examA, 2022); Map(db, shared, _examB, 2022);   // counted once for EACH exam

            var inactive = Qbq(db, "inactive", active: false);
            Map(db, inactive, _examA, 2020);
            var testOnly = Qbq(db, "test only", testOnly: true);
            Map(db, testOnly, _examA, 2020); Map(db, testOnly, _examB, 2020);

            // Mirrors are excluded whatever the paper status (unlike the student catalog's visibility rule).
            var draftMirror = Qbq(db, "draft mirror"); Source(db, draftMirror, draft); Map(db, draftMirror, _examA, 2019);
            var pendingMirror = Qbq(db, "pending mirror"); Source(db, pendingMirror, pending); Map(db, pendingMirror, _examB, 2019);
            var publishedMirror = Qbq(db, "published mirror"); Source(db, publishedMirror, published); Map(db, publishedMirror, _examA, 2018);
            var nullPaperMirror = Qbq(db, "mirror without a paper"); Source(db, nullPaperMirror, null); Map(db, nullPaperMirror, _examB, 2018);

            var dBank = Qbq(db, "exam D bank question");
            Map(db, dBank, _examD, 2020);
            db.Questions.Add(new Question { ExamId = _examD.Id });          // legacy PYP half for D

            db.SaveChanges();
        }

        // The previous implementation, verbatim (bank half only).
        private static async Task<Dictionary<Guid, int>> RefBankCounts(ScoramDbContext db)
        {
            var mirroredBankQuestionIds = await db.Questions
                .Where(q => q.MirroredToQuestionBankQuestionId != null)
                .Select(q => q.MirroredToQuestionBankQuestionId!.Value)
                .Distinct()
                .ToListAsync();
            var mirroredSet = mirroredBankQuestionIds.ToHashSet();

            var pairs = await db.QuestionBankQuestions
                .Where(x => x.IsActive && !x.IsTestOnly)
                .SelectMany(x => x.ExamMappings.Select(m => new { QuestionId = x.Id, m.ExamId }))
                .Distinct()
                .ToListAsync();

            return pairs
                .Where(p => !mirroredSet.Contains(p.QuestionId))
                .GroupBy(p => p.ExamId)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        // PYP half exactly as List/AdminList compute it.
        private static async Task<int> PypCount(ScoramDbContext db, Guid examId) =>
            await db.Questions.CountAsync(q => q.ExamId == examId)
            + await db.Papers.Where(p => p.ExamId == examId).SelectMany(p => p.Questions).CountAsync();

        private static List<ExamResponseDto> Items(ActionResult<List<ExamResponseDto>> r) =>
            (List<ExamResponseDto>)((OkObjectResult)r.Result!).Value!;

        [Fact]
        public async Task List_BankHalf_MatchesHandComputedNumbers()
        {
            using var db = NewContext();
            var byId = Items(await NewController(db).List(null)).ToDictionary(e => e.Id, e => e.QuestionCount);

            // PYP half for A: the three mirror sources that sit on A's papers (draft, pending, published)
            // -- unchanged behaviour, added on top of the bank half.
            Assert.Equal(2 + 3, byId[_examA.Id]);
            Assert.Equal(1, byId[_examB.Id]);
            Assert.Equal(0, byId[_examC.Id]);
            Assert.Equal(1 + 1, byId[_examD.Id]);   // 1 bank + 1 legacy PYP
        }

        [Fact]
        public async Task List_AndAdminList_MatchPreviousInMemoryAlgorithm_ForEveryExam()
        {
            using var db = NewContext();
            var reference = await RefBankCounts(db);

            foreach (var items in new[]
                     {
                         Items(await NewController(db).List(null)),
                         Items(await NewController(db).AdminList(null))
                     })
            {
                Assert.Equal(4, items.Count);
                foreach (var e in items)
                    Assert.Equal(await PypCount(db, e.Id) + reference.GetValueOrDefault(e.Id), e.QuestionCount);
            }
        }

        [Fact]
        public async Task List_SearchFilter_DoesNotChangeAListedExamsCount()
        {
            using var db = NewContext();
            var all = Items(await NewController(db).List(null)).ToDictionary(e => e.Id, e => e.QuestionCount);

            var filtered = Items(await NewController(db).List(null, "Exam B"));
            var only = Assert.Single(filtered);
            Assert.Equal(_examB.Id, only.Id);
            Assert.Equal(all[_examB.Id], only.QuestionCount);

            // A search that matches nothing returns nothing (and must not throw on the empty id list).
            Assert.Empty(Items(await NewController(db).List(null, "zzz-no-such-exam")));
        }

        [Fact]
        public async Task List_ResponseShape_IsUnchanged()
        {
            using var db = NewContext();
            var items = Items(await NewController(db).List(null));

            Assert.Equal(items.OrderBy(e => e.Name).Select(e => e.Id), items.Select(e => e.Id)); // still ordered by Name
            Assert.All(items, e => Assert.Null(e.BusinessId));                                  // public list never fills it
        }

        [Fact]
        public async Task List_CancelledToken_Throws()
        {
            // Relies on the in-memory provider observing an already-cancelled token inside ToListAsync(ct).
            // If this one ever fails while the SQL-Server behaviour is fine, it is the provider, not the
            // controller -- see the report's checklist for the manual cancel check.
            using var db = NewContext();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NewController(db).List(null, null, cts.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NewController(db).AdminList(null, cts.Token));
        }
    }
}
