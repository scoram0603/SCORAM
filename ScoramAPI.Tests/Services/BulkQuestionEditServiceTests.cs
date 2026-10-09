using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Models;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Services
{
    public class BulkQuestionEditServiceTests
    {
        private static readonly Guid Admin = Guid.NewGuid();

        private static ScoramDbContext NewDb() => new(new DbContextOptionsBuilder<ScoramDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static Paper P(ScoramDbContext db, PaperStatus st = PaperStatus.Draft, int year = 2024)
        {
            // Real Exam row: the service Includes Paper.Exam (required), and InMemory inner-joins it.
            var e = new Exam { Name = "UGC NET" }; db.Exams.Add(e);
            var p = new Paper { ExamId = e.Id, Year = year, Status = st, CreatedByAdminId = Admin }; db.Papers.Add(p); return p;
        }

        private static Question Q(ScoramDbContext db, Paper p, int n)
        {
            var q = new Question { PaperId = p.Id, QuestionNumber = n, Subject = "Math", Topic = "Algebra", QuestionText = "Q" + n,
                OptionA = "a", OptionB = "b", OptionC = "c", OptionD = "d", CorrectOption = OptionLetter.A };
            db.Questions.Add(q); return q;
        }

        private static PaperQuestionBankLink L(ScoramDbContext db, Paper p, int n)
        {
            // Real QB question + subject + topic: the union listing projects through all three (inner joins).
            var sub = new QuestionBankSubject { Name = "Math", CreatedByAdminId = Admin };
            var top = new QuestionBankTopic { SubjectId = sub.Id, Name = "Algebra", CreatedByAdminId = Admin };
            var qb = new QuestionBankQuestion { QuestionText = "QB" + n, NormalizedQuestionText = "qb" + n, OptionA = "a", OptionB = "b", OptionC = "c", OptionD = "d",
                SubjectId = sub.Id, TopicId = top.Id, CreatedByAdminId = Admin };
            db.AddRange(sub, top, qb);
            var l = new PaperQuestionBankLink { PaperId = p.Id, QuestionBankQuestionId = qb.Id, QuestionNumber = n, LinkedByAdminId = Admin }; db.PaperQuestionBankLinks.Add(l); return l;
        }

        private static SharedStimulus S(ScoramDbContext db, SharedStimulusStatus st = SharedStimulusStatus.Active)
        { var s = new SharedStimulus { Title = "Passage", Status = st, CreatedByAdminId = Admin }; db.SharedStimuli.Add(s); return s; }

        private static StimulusTargetDto T(Question q) => new() { QuestionId = q.Id };
        private static StimulusTargetDto T(PaperQuestionBankLink l) => new() { LinkId = l.Id };

        private static BulkRequestDto Req(IEnumerable<Paper> papers, IEnumerable<StimulusTargetDto> targets, BulkOperationDto op) =>
            new() { PaperIds = papers.Select(p => p.Id).ToList(), Targets = targets.ToList(), Operation = op };

        private static BulkOperationDto SetSubject(string subject) =>
            new() { Type = "SetFields", Fields = new BulkFieldChangesDto { Subject = subject } };

        [Fact]
        public async Task SetFields_AcrossPapers_PreviewMatchesApply_AndPersists()
        {
            using var db = NewDb(); var p1 = P(db); var p2 = P(db, year: 2023);
            var q1 = Q(db, p1, 1); var q2 = Q(db, p1, 2); var q3 = Q(db, p2, 1); db.SaveChangesAsync().GetAwaiter().GetResult();
            var svc = new BulkQuestionEditService(db);
            var req = Req(new[] { p1, p2 }, new[] { T(q1), T(q2), T(q3) }, SetSubject("Reasoning"));

            var prev = await svc.PreviewAsync(req);
            Assert.True(prev.CanApply); Assert.Equal(3, prev.ChangedCount); Assert.Equal(2, prev.PaperCount);
            Assert.All(db.Questions, q => Assert.Equal("Math", q.Subject)); // preview wrote nothing

            var res = await svc.ApplyAsync(req, Admin);
            Assert.True(res.Success); Assert.Equal(3, res.QuestionsChanged);
            Assert.All(db.Questions.AsNoTracking(), q => Assert.Equal("Reasoning", q.Subject));
            Assert.Equal(3, db.Questions.Count()); // nothing recreated
        }

        [Fact]
        public async Task SetFields_PublishedPaper_BlocksEverything()
        {
            using var db = NewDb(); var draft = P(db); var pub = P(db, PaperStatus.Published);
            var a = Q(db, draft, 1); var b = Q(db, pub, 1); db.SaveChangesAsync().GetAwaiter().GetResult();
            var svc = new BulkQuestionEditService(db);
            var res = await svc.ApplyAsync(Req(new[] { draft, pub }, new[] { T(a), T(b) }, SetSubject("X")), Admin);
            Assert.False(res.Success); Assert.Contains("published", res.Errors[0]);
            Assert.All(db.Questions.AsNoTracking(), q => Assert.Equal("Math", q.Subject)); // all-or-nothing
        }

        [Fact]
        public async Task SetFields_OnQuestionBankLink_IsRejected_ButEmptySubjectToo()
        {
            using var db = NewDb(); var p = P(db); var q = Q(db, p, 1); var l = L(db, p, 2); db.SaveChangesAsync().GetAwaiter().GetResult();
            var svc = new BulkQuestionEditService(db);
            var r1 = await svc.PreviewAsync(Req(new[] { p }, new[] { T(q), T(l) }, SetSubject("X")));
            Assert.False(r1.CanApply); Assert.Contains("Question Bank", r1.Errors[0]);
            var r2 = await svc.PreviewAsync(Req(new[] { p }, new[] { T(q) }, SetSubject("  ")));
            Assert.False(r2.CanApply);
        }

        [Fact]
        public async Task Attach_AcrossPapers_QuestionsAndLinks_ThenIdempotent_AndAllowedOnPublished()
        {
            using var db = NewDb(); var p1 = P(db); var p2 = P(db, PaperStatus.Published);
            var q = Q(db, p1, 1); var l = L(db, p2, 1); var s = S(db); db.SaveChangesAsync().GetAwaiter().GetResult();
            var svc = new BulkQuestionEditService(db);
            var op = new BulkOperationDto { Type = "AttachStimulus", StimulusId = s.Id };
            var req = Req(new[] { p1, p2 }, new[] { T(q), T(l) }, op);

            var first = await svc.ApplyAsync(req, Admin);
            Assert.True(first.Success); Assert.Equal(2, first.QuestionsChanged);
            Assert.Equal(2, db.PaperQuestionStimuli.AsNoTracking().Count());

            var second = await svc.ApplyAsync(req, Admin);
            Assert.True(second.Success); Assert.Equal(0, second.QuestionsChanged);
            Assert.Equal(2, db.PaperQuestionStimuli.AsNoTracking().Count()); // no duplicates
        }

        [Fact]
        public async Task Attach_ArchivedOrUnknownStimulus_IsError_NothingWritten()
        {
            using var db = NewDb(); var p = P(db); var q = Q(db, p, 1); var s = S(db, SharedStimulusStatus.Archived); db.SaveChangesAsync().GetAwaiter().GetResult();
            var svc = new BulkQuestionEditService(db);
            var r1 = await svc.ApplyAsync(Req(new[] { p }, new[] { T(q) }, new() { Type = "AttachStimulus", StimulusId = s.Id }), Admin);
            var r2 = await svc.ApplyAsync(Req(new[] { p }, new[] { T(q) }, new() { Type = "AttachStimulus", StimulusId = Guid.NewGuid() }), Admin);
            Assert.False(r1.Success); Assert.False(r2.Success);
            Assert.Empty(db.PaperQuestionStimuli);
        }

        [Fact]
        public async Task Replace_RepointsAndMerges_NoStimulusDeleted()
        {
            using var db = NewDb(); var p = P(db); var q1 = Q(db, p, 1); var q2 = Q(db, p, 2); var s1 = S(db); var s2 = S(db); db.SaveChangesAsync().GetAwaiter().GetResult();
            var svc = new BulkQuestionEditService(db);
            var targets = new[] { T(q1), T(q2) };
            await svc.ApplyAsync(Req(new[] { p }, targets, new() { Type = "AttachStimulus", StimulusId = s1.Id }), Admin);
            await svc.ApplyAsync(Req(new[] { p }, new[] { T(q1) }, new() { Type = "AttachStimulus", StimulusId = s2.Id }), Admin); // q1 already has s2

            var res = await svc.ApplyAsync(Req(new[] { p }, targets, new() { Type = "ReplaceStimulus", FromStimulusId = s1.Id, ToStimulusId = s2.Id }), Admin);
            Assert.True(res.Success); Assert.Equal(2, res.QuestionsChanged);
            var rows = db.PaperQuestionStimuli.AsNoTracking().ToList();
            Assert.Equal(2, rows.Count); Assert.All(rows, r => Assert.Equal(s2.Id, r.SharedStimulusId));
            Assert.Equal(2, db.SharedStimuli.Count());

            var same = await svc.PreviewAsync(Req(new[] { p }, targets, new() { Type = "ReplaceStimulus", FromStimulusId = s2.Id, ToStimulusId = s2.Id }));
            Assert.False(same.CanApply);
        }

        [Fact]
        public async Task Remove_Specific_And_All_KeepStimulusAndQuestions()
        {
            using var db = NewDb(); var p = P(db); var q = Q(db, p, 1); var s1 = S(db); var s2 = S(db); db.SaveChangesAsync().GetAwaiter().GetResult();
            var svc = new BulkQuestionEditService(db);
            await svc.ApplyAsync(Req(new[] { p }, new[] { T(q) }, new() { Type = "AttachStimulus", StimulusId = s1.Id }), Admin);
            await svc.ApplyAsync(Req(new[] { p }, new[] { T(q) }, new() { Type = "AttachStimulus", StimulusId = s2.Id }), Admin);

            var one = await svc.ApplyAsync(Req(new[] { p }, new[] { T(q) }, new() { Type = "RemoveStimulus", StimulusId = s1.Id }), Admin);
            Assert.Equal(1, one.QuestionsChanged); Assert.Single(db.PaperQuestionStimuli.AsNoTracking());
            var all = await svc.ApplyAsync(Req(new[] { p }, new[] { T(q) }, new() { Type = "RemoveStimulus" }), Admin);
            Assert.Equal(1, all.QuestionsChanged); Assert.Empty(db.PaperQuestionStimuli);
            Assert.Equal(2, db.SharedStimuli.Count()); Assert.Equal(1, db.Questions.Count());
        }

        [Fact]
        public async Task Selection_QuestionOutsideSelectedPapers_OrBadTarget_Throws()
        {
            using var db = NewDb(); var p1 = P(db); var p2 = P(db); var q = Q(db, p2, 1); db.SaveChangesAsync().GetAwaiter().GetResult();
            var svc = new BulkQuestionEditService(db);
            await Assert.ThrowsAsync<BulkEditException>(() => svc.PreviewAsync(Req(new[] { p1 }, new[] { T(q) }, SetSubject("X"))));
            await Assert.ThrowsAsync<BulkEditException>(() => svc.PreviewAsync(Req(new[] { p2 }, new[] { new StimulusTargetDto() }, SetSubject("X"))));
            await Assert.ThrowsAsync<BulkEditException>(() => svc.PreviewAsync(Req(new[] { p2 }, new StimulusTargetDto[0], SetSubject("X"))));
            await Assert.ThrowsAsync<BulkEditException>(() => svc.PreviewAsync(Req(new Paper[0], new[] { T(q) }, SetSubject("X"))));
        }

        [Fact]
        public async Task AllMatching_RequiresMatchingExpectedCount()
        {
            using var db = NewDb(); var p = P(db); Q(db, p, 1); Q(db, p, 2); L(db, p, 3); db.SaveChangesAsync().GetAwaiter().GetResult();
            var svc = new BulkQuestionEditService(db);
            BulkRequestDto R(int? expected) => new() { PaperIds = { p.Id }, AllMatching = new BulkQuestionFilterDto(), ExpectedCount = expected, Operation = new() { Type = "AttachStimulus", StimulusId = Guid.NewGuid() } };
            await Assert.ThrowsAsync<BulkEditException>(() => svc.PreviewAsync(R(null)));
            await Assert.ThrowsAsync<BulkEditException>(() => svc.PreviewAsync(R(2)));
            var s = S(db); db.SaveChangesAsync().GetAwaiter().GetResult();
            var ok = R(3); ok.Operation.StimulusId = s.Id;
            var prev = await svc.PreviewAsync(ok);
            Assert.Equal(3, prev.QuestionCount); Assert.True(prev.CanApply);
        }

        [Fact]
        public async Task IndividualEdits_OneInvalid_NothingApplied()
        {
            using var db = NewDb(); var p = P(db); var a = Q(db, p, 1); var b = Q(db, p, 2); db.SaveChangesAsync().GetAwaiter().GetResult();
            var svc = new BulkQuestionEditService(db);
            var op = new BulkOperationDto { Type = "IndividualEdits", Edits = new()
            {
                new BulkQuestionEditDto { QuestionId = a.Id, QuestionText = "New text" },
                new BulkQuestionEditDto { QuestionId = b.Id, CorrectOption = "E" }
            } };
            var res = await svc.ApplyAsync(new BulkRequestDto { PaperIds = { p.Id }, Operation = op }, Admin);
            Assert.False(res.Success); Assert.Contains("correct option", res.Errors[0]);
            Assert.Equal("Q1", db.Questions.AsNoTracking().First(q => q.Id == a.Id).QuestionText); // valid one NOT applied either
        }

        [Fact]
        public async Task IndividualEdits_Valid_AppliesTextOptionsAndCorrectOption()
        {
            using var db = NewDb(); var p = P(db); var a = Q(db, p, 1); db.SaveChangesAsync().GetAwaiter().GetResult();
            var svc = new BulkQuestionEditService(db);
            var op = new BulkOperationDto { Type = "IndividualEdits", Edits = new()
            { new BulkQuestionEditDto { QuestionId = a.Id, QuestionText = "New", OptionB = "bb", CorrectOption = "c", DifficultyLevel = "Hard" } } };
            var res = await svc.ApplyAsync(new BulkRequestDto { PaperIds = { p.Id }, Operation = op }, Admin);
            Assert.True(res.Success);
            var q = db.Questions.AsNoTracking().Single();
            Assert.Equal("New", q.QuestionText); Assert.Equal("bb", q.OptionB); Assert.Equal(OptionLetter.C, q.CorrectOption); Assert.Equal(DifficultyLevel.Hard, q.DifficultyLevel);
            Assert.Equal("a", q.OptionA); // untouched fields stay
        }

        [Fact]
        public async Task Images_SetRejectsUnsafeUrl_RemoveClearsReferenceOnly()
        {
            using var db = NewDb(); var p = P(db); var q = Q(db, p, 1); db.SaveChangesAsync().GetAwaiter().GetResult();
            var svc = new BulkQuestionEditService(db);
            var bad = await svc.PreviewAsync(Req(new[] { p }, new[] { T(q) }, new() { Type = "SetImage", ImageField = "Question", ImageUrl = "https://evil.com/a.png" }));
            Assert.False(bad.CanApply);

            var set = await svc.ApplyAsync(Req(new[] { p }, new[] { T(q) }, new() { Type = "SetImage", ImageField = "Question", ImageUrl = "/uploads/question-images/a.png" }), Admin);
            Assert.True(set.Success);
            Assert.Equal("/uploads/question-images/a.png", db.Questions.AsNoTracking().Single().QuestionImageUrl);

            var rm = await svc.ApplyAsync(Req(new[] { p }, new[] { T(q) }, new() { Type = "RemoveImage", ImageField = "Question" }), Admin);
            Assert.True(rm.Success); Assert.Null(db.Questions.AsNoTracking().Single().QuestionImageUrl);
        }

        [Fact]
        public async Task ListQuestions_UnionsBothSources_OrderedAndPaged()
        {
            using var db = NewDb(); var p = P(db);
            Q(db, p, 1); Q(db, p, 3); L(db, p, 2);
            db.SaveChangesAsync().GetAwaiter().GetResult();
            var res = await new BulkQuestionEditService(db).ListQuestionsAsync(new[] { p.Id }, new BulkQuestionFilterDto(), 1, 10);
            Assert.Equal(3, res.TotalCount);
            Assert.Equal(new int?[] { 1, 2, 3 }, res.Items.Select(i => i.QuestionNumber).ToArray());
            Assert.Equal("QuestionBank", res.Items[1].Source);
        }
    }
}
