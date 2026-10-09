using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Models;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Services
{
    public class StimulusLinkServiceTests
    {
        private static readonly Guid Admin = Guid.NewGuid();

        private static ScoramDbContext NewDb() => new(new DbContextOptionsBuilder<ScoramDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static (Guid paperId, List<Guid> qIds, Guid linkId, Guid stimId) Seed(ScoramDbContext db, SharedStimulusStatus status = SharedStimulusStatus.Active)
        {
            var paper = new Paper { CreatedByAdminId = Admin };
            var qs = Enumerable.Range(1, 5).Select(n => new Question { PaperId = paper.Id, QuestionNumber = n, QuestionText = "Q" + n }).ToList();
            var link = new PaperQuestionBankLink { PaperId = paper.Id, QuestionBankQuestionId = Guid.NewGuid(), QuestionNumber = 6, LinkedByAdminId = Admin };
            var stim = new SharedStimulus { Title = "Passage", Status = status, CreatedByAdminId = Admin };
            db.AddRange(paper, link, stim); db.AddRange(qs); db.SaveChangesAsync().GetAwaiter().GetResult();
            return (paper.Id, qs.Select(q => q.Id).ToList(), link.Id, stim.Id);
        }

        private static List<StimulusTargetDto> T(IEnumerable<Guid> q, Guid? link = null)
        {
            var l = q.Select(id => new StimulusTargetDto { QuestionId = id }).ToList();
            if (link != null) l.Add(new StimulusTargetDto { LinkId = link });
            return l;
        }

        [Fact]
        public async Task Attach_LinksQ1toQ5_AndBothSources_WithoutTouchingQuestions()
        {
            using var db = NewDb(); var (p, qs, link, s) = Seed(db);
            var r = await new StimulusLinkService(db).AttachAsync(p, s, T(qs, link), Admin);
            Assert.Equal(6, r.Changed);
            Assert.Equal(6, db.PaperQuestionStimuli.Count());
            Assert.Equal(5, db.Questions.Count());
            Assert.All(db.Questions, q => Assert.StartsWith("Q", q.QuestionText));
        }

        [Fact]
        public async Task Attach_Twice_DoesNotDuplicate()
        {
            using var db = NewDb(); var (p, qs, _, s) = Seed(db);
            var svc = new StimulusLinkService(db);
            await svc.AttachAsync(p, s, T(qs), Admin);
            var r = await svc.AttachAsync(p, s, T(qs), Admin);
            Assert.Equal(0, r.Changed); Assert.Equal(5, r.Skipped);
            Assert.Equal(5, db.PaperQuestionStimuli.Count());
        }

        [Fact]
        public async Task Attach_RejectsArchivedStimulus_UnknownStimulus_AndForeignQuestion()
        {
            using var db = NewDb(); var (p, qs, _, s) = Seed(db, SharedStimulusStatus.Archived);
            var svc = new StimulusLinkService(db);
            await Assert.ThrowsAsync<StimulusLinkException>(() => svc.AttachAsync(p, s, T(qs), Admin));
            await Assert.ThrowsAsync<StimulusLinkException>(() => svc.AttachAsync(p, Guid.NewGuid(), T(qs), Admin));

            using var db2 = NewDb(); var (p2, _, _, s2) = Seed(db2);
            var other = new Question { PaperId = Guid.NewGuid(), QuestionNumber = 1, QuestionText = "x" };
            db2.Questions.Add(other); db2.SaveChangesAsync().GetAwaiter().GetResult();
            var ex = await Assert.ThrowsAsync<StimulusLinkException>(() => new StimulusLinkService(db2).AttachAsync(p2, s2, T(new[] { other.Id }), Admin));
            Assert.Equal(0, db2.PaperQuestionStimuli.Count()); // nothing partial
        }

        [Fact]
        public async Task Attach_RejectsBadTargets()
        {
            using var db = NewDb(); var (p, qs, link, s) = Seed(db);
            var svc = new StimulusLinkService(db);
            await Assert.ThrowsAsync<StimulusLinkException>(() => svc.AttachAsync(p, s, new List<StimulusTargetDto>(), Admin));
            await Assert.ThrowsAsync<StimulusLinkException>(() => svc.AttachAsync(p, s, new[] { new StimulusTargetDto { QuestionId = qs[0], LinkId = link } }, Admin));
            await Assert.ThrowsAsync<StimulusLinkException>(() => svc.AttachAsync(p, s, new[] { new StimulusTargetDto() }, Admin));
        }

        [Fact]
        public async Task Detach_RemovesOnlyRelationship_KeepsStimulusAndQuestions()
        {
            using var db = NewDb(); var (p, qs, _, s) = Seed(db);
            var svc = new StimulusLinkService(db);
            await svc.AttachAsync(p, s, T(qs), Admin);
            var r = await svc.DetachAsync(p, s, T(qs.Take(2)));
            Assert.Equal(2, r.Changed);
            Assert.Equal(3, db.PaperQuestionStimuli.Count());
            Assert.Equal(1, db.SharedStimuli.Count()); Assert.Equal(5, db.Questions.Count());
        }

        [Fact]
        public async Task Replace_RepointsLinks_AndMergesWhenTargetAlreadyLinked()
        {
            using var db = NewDb(); var (p, qs, _, s1) = Seed(db);
            var s2 = new SharedStimulus { Title = "Other", CreatedByAdminId = Admin }; db.SharedStimuli.Add(s2); db.SaveChangesAsync().GetAwaiter().GetResult();
            var svc = new StimulusLinkService(db);
            await svc.AttachAsync(p, s1, T(qs), Admin);
            await svc.AttachAsync(p, s2.Id, T(qs.Take(1)), Admin);   // Q1 already has s2

            var r = await svc.ReplaceAsync(p, s1, s2.Id, T(qs));
            Assert.Equal(5, r.Changed);
            Assert.Equal(5, db.PaperQuestionStimuli.Count());
            Assert.All(db.PaperQuestionStimuli, x => Assert.Equal(s2.Id, x.SharedStimulusId));
            Assert.Equal(2, db.SharedStimuli.Count()); // neither stimulus deleted
        }

        [Fact]
        public async Task List_ReturnsOrderedLinksForPaperOnly()
        {
            using var db = NewDb(); var (p, qs, link, s) = Seed(db);
            var svc = new StimulusLinkService(db);
            await svc.AttachAsync(p, s, T(qs.Take(2), link), Admin);
            var list = await svc.ListForPaperAsync(p);
            Assert.Equal(new int?[] { 1, 2, 6 }, list.Select(x => x.QuestionNumber).ToArray());
            Assert.Empty(await svc.ListForPaperAsync(Guid.NewGuid()));
        }
    }
}
