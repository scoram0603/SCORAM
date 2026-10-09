using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Models;
using Xunit;

namespace ScoramAPI.Tests.Services
{
    // Logic-level check of the SaveChanges hook (InMemory does not enforce FKs; the real FK
    // behaviour is verified by running the migration against SQL Server).
    public class StimulusLinkCleanupTests
    {
        private static ScoramDbContext NewDb() => new(new DbContextOptionsBuilder<ScoramDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static (Guid paperId, Guid linkId, Guid stimId) Seed(ScoramDbContext db)
        {
            var admin = Guid.NewGuid();
            var paper = new Paper { CreatedByAdminId = admin };
            var link = new PaperQuestionBankLink { PaperId = paper.Id, QuestionBankQuestionId = Guid.NewGuid(), QuestionNumber = 1, LinkedByAdminId = admin };
            var stim = new SharedStimulus { Title = "Passage", CreatedByAdminId = admin };
            db.AddRange(paper, link, stim,
                new PaperQuestionStimulus { SharedStimulusId = stim.Id, PaperQuestionBankLinkId = link.Id, CreatedByAdminId = admin });
            db.SaveChangesAsync().GetAwaiter().GetResult();
            return (paper.Id, link.Id, stim.Id);
        }

        [Fact]
        public async Task UnmappingLink_RemovesOnlyItsStimulusRows()
        {
            using var db = NewDb();
            var (_, linkId, stimId) = Seed(db);
            db.PaperQuestionBankLinks.Remove(await db.PaperQuestionBankLinks.FirstAsync(l => l.Id == linkId));
            await db.SaveChangesAsync();
            Assert.Empty(db.PaperQuestionStimuli);
            Assert.NotNull(await db.SharedStimuli.FindAsync(stimId)); // stimulus itself survives
        }

        [Fact]
        public async Task DeletingPaper_RemovesStimulusRowsOfItsLinks()
        {
            using var db = NewDb();
            var (paperId, _, stimId) = Seed(db);
            db.Papers.Remove(await db.Papers.FirstAsync(p => p.Id == paperId));
            await db.SaveChangesAsync();
            Assert.Empty(db.PaperQuestionStimuli);
            Assert.NotNull(await db.SharedStimuli.FindAsync(stimId));
        }

        [Fact]
        public async Task UnrelatedSaves_LeaveStimulusRowsAlone()
        {
            using var db = NewDb();
            Seed(db);
            db.Papers.Add(new Paper { CreatedByAdminId = Guid.NewGuid() });
            await db.SaveChangesAsync();
            Assert.Single(db.PaperQuestionStimuli);
        }
    }
}
