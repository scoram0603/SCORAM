using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Models;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Services
{
    public class PaperContentServiceTests
    {
        private static readonly Guid Admin = Guid.NewGuid();

        private static ScoramDbContext NewDb() => new(new DbContextOptionsBuilder<ScoramDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static string Blocks(string text) => ContentBlocksJsonHelper.ValidateAndSerialize($"[{{\"type\":\"text\",\"content\":\"{text}\"}}]")!;

        private static SharedStimulus Stim(ScoramDbContext db, string title, SharedStimulusStatus st = SharedStimulusStatus.Active)
        { var s = new SharedStimulus { Title = title, Status = st, ContentBlocksJson = Blocks(title), CreatedByAdminId = Admin }; db.SharedStimuli.Add(s); return s; }

        private static void Link(ScoramDbContext db, SharedStimulus s, Question q, int order = 0) =>
            db.PaperQuestionStimuli.Add(new PaperQuestionStimulus { SharedStimulusId = s.Id, QuestionId = q.Id, DisplayOrder = order, CreatedByAdminId = Admin });

        private static (StudentTestResult attempt, TestAttemptStartResponseDto dto) Attempt(Guid? paperId, IEnumerable<StudentAnswer> answers)
        {
            var list = answers.ToList();
            var attempt = new StudentTestResult { PaperId = paperId, Answers = list };
            var dto = new TestAttemptStartResponseDto
            { Questions = list.Select(a => new TestAttemptQuestionDto { Id = a.Id, QuestionOrder = a.QuestionOrder }).ToList() };
            return (attempt, dto);
        }

        [Fact]
        public async Task Enrich_NonPaperAttempt_IsNoOp()
        {
            using var db = NewDb();
            var (a, dto) = Attempt(null, new[] { new StudentAnswer { QuestionId = Guid.NewGuid(), QuestionOrder = 1 } });
            await new PaperContentService(db).EnrichAsync(dto, a);
            Assert.Empty(dto.Stimuli); Assert.Empty(dto.PaperInstructions); Assert.All(dto.Questions, q => Assert.Empty(q.StimulusIds));
        }

        [Fact]
        public async Task Enrich_PaperWithoutContent_LeavesResponseUntouched()
        {
            using var db = NewDb(); var p = new Paper { CreatedByAdminId = Admin }; var q = new Question { PaperId = p.Id, QuestionNumber = 1 };
            db.AddRange(p, q); db.SaveChangesAsync().GetAwaiter().GetResult();
            var (a, dto) = Attempt(p.Id, new[] { new StudentAnswer { QuestionId = q.Id, QuestionOrder = 1 } });
            await new PaperContentService(db).EnrichAsync(dto, a);
            Assert.Empty(dto.Stimuli); Assert.Empty(dto.PaperInstructions); Assert.Empty(dto.Questions[0].StimulusIds);
        }

        [Fact]
        public async Task Enrich_OneStimulusForFiveQuestions_IsSentOnce_AndReferencedByAll()
        {
            using var db = NewDb(); var p = new Paper { CreatedByAdminId = Admin }; db.Papers.Add(p);
            var qs = Enumerable.Range(1, 6).Select(n => new Question { PaperId = p.Id, QuestionNumber = n }).ToList(); db.AddRange(qs);
            var s = Stim(db, "Passage"); foreach (var q in qs.Take(5)) Link(db, s, q); db.SaveChangesAsync().GetAwaiter().GetResult();
            var (a, dto) = Attempt(p.Id, qs.Select((q, i) => new StudentAnswer { QuestionId = q.Id, QuestionOrder = i + 1 }));

            await new PaperContentService(db).EnrichAsync(dto, a);

            Assert.Single(dto.Stimuli); Assert.Equal(s.Id, dto.Stimuli[0].Id); Assert.Single(dto.Stimuli[0].ContentBlocks);
            Assert.All(dto.Questions.Take(5), q => Assert.Equal(new[] { s.Id }, q.StimulusIds));
            Assert.Empty(dto.Questions[5].StimulusIds);
        }

        [Fact]
        public async Task Enrich_HidesArchivedStimulus_AndArchivedInstructions_OrdersActiveInstructions()
        {
            using var db = NewDb(); var p = new Paper { CreatedByAdminId = Admin }; var q = new Question { PaperId = p.Id, QuestionNumber = 1 };
            db.AddRange(p, q); var archived = Stim(db, "Old", SharedStimulusStatus.Archived); Link(db, archived, q);
            db.PaperInstructions.AddRange(
                new PaperInstruction { PaperId = p.Id, Title = "second", DisplayOrder = 1, ContentBlocksJson = Blocks("b"), CreatedByAdminId = Admin },
                new PaperInstruction { PaperId = p.Id, Title = "first", DisplayOrder = 0, ContentBlocksJson = Blocks("a"), CreatedByAdminId = Admin },
                new PaperInstruction { PaperId = p.Id, Title = "gone", DisplayOrder = 2, Status = PaperInstructionStatus.Archived, ContentBlocksJson = Blocks("c"), CreatedByAdminId = Admin });
            db.SaveChangesAsync().GetAwaiter().GetResult();
            var (a, dto) = Attempt(p.Id, new[] { new StudentAnswer { QuestionId = q.Id, QuestionOrder = 1 } });

            await new PaperContentService(db).EnrichAsync(dto, a);

            Assert.Empty(dto.Stimuli); Assert.Empty(dto.Questions[0].StimulusIds);
            Assert.Equal(new[] { "first", "second" }, dto.PaperInstructions.Select(i => i.Title).ToArray());
        }

        [Fact]
        public async Task Enrich_QuestionBankAnswer_MapsThroughThisPapersLinkOnly()
        {
            using var db = NewDb(); var p1 = new Paper { CreatedByAdminId = Admin }; var p2 = new Paper { CreatedByAdminId = Admin }; db.AddRange(p1, p2);
            var qbId = Guid.NewGuid();
            var l1 = new PaperQuestionBankLink { PaperId = p1.Id, QuestionBankQuestionId = qbId, QuestionNumber = 1, LinkedByAdminId = Admin };
            var l2 = new PaperQuestionBankLink { PaperId = p2.Id, QuestionBankQuestionId = qbId, QuestionNumber = 1, LinkedByAdminId = Admin };
            var s = Stim(db, "Table"); db.AddRange(l1, l2);
            db.PaperQuestionStimuli.Add(new PaperQuestionStimulus { SharedStimulusId = s.Id, PaperQuestionBankLinkId = l1.Id, CreatedByAdminId = Admin });
            db.SaveChangesAsync().GetAwaiter().GetResult();

            var (a1, d1) = Attempt(p1.Id, new[] { new StudentAnswer { QuestionBankQuestionId = qbId, QuestionOrder = 1 } });
            var (a2, d2) = Attempt(p2.Id, new[] { new StudentAnswer { QuestionBankQuestionId = qbId, QuestionOrder = 1 } });
            var svc = new PaperContentService(db);
            await svc.EnrichAsync(d1, a1); await svc.EnrichAsync(d2, a2);

            Assert.Equal(new[] { s.Id }, d1.Questions[0].StimulusIds);
            Assert.Empty(d2.Questions[0].StimulusIds); // same canonical QB question, other paper: no stimulus
            Assert.Empty(d2.Stimuli);
        }

        [Fact]
        public async Task Preview_OrdersByQuestionNumber_GroupsStimuli_AndUnknownPaperIsNull()
        {
            using var db = NewDb(); var p = new Paper { CreatedByAdminId = Admin }; db.Papers.Add(p);
            var q1 = new Question { PaperId = p.Id, QuestionNumber = 1, QuestionText = "one" };
            var q3 = new Question { PaperId = p.Id, QuestionNumber = 3, QuestionText = "three" };
            var qb = new QuestionBankQuestion { QuestionText = "two" };
            var link = new PaperQuestionBankLink { PaperId = p.Id, QuestionBankQuestionId = qb.Id, QuestionNumber = 2, LinkedByAdminId = Admin };
            db.AddRange(q1, q3, qb, link);
            var s = Stim(db, "Passage"); Link(db, s, q1); Link(db, s, q3);
            db.PaperQuestionStimuli.Add(new PaperQuestionStimulus { SharedStimulusId = s.Id, PaperQuestionBankLinkId = link.Id, CreatedByAdminId = Admin });
            db.SaveChangesAsync().GetAwaiter().GetResult();

            var svc = new PaperContentService(db);
            var prev = await svc.GetPreviewAsync(p.Id);
            Assert.NotNull(prev);
            Assert.Equal(new[] { "one", "two", "three" }, prev!.Questions.Select(q => q.QuestionText).ToArray());
            Assert.Single(prev.Stimuli);
            Assert.All(prev.Questions, q => Assert.Equal(new[] { s.Id }, q.StimulusIds));
            Assert.Null(await svc.GetPreviewAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task EnrichResult_AttachesStimulusToReviewRows_AndIsNoOpForNonPaper()
        {
            using var db = NewDb(); var p = new Paper { CreatedByAdminId = Admin }; db.Papers.Add(p);
            var qs = Enumerable.Range(1, 3).Select(n => new Question { PaperId = p.Id, QuestionNumber = n }).ToList(); db.AddRange(qs);
            var s = Stim(db, "Passage"); Link(db, s, qs[0]); Link(db, s, qs[1]); db.SaveChangesAsync().GetAwaiter().GetResult();
            var answers = qs.Select((q, i) => new StudentAnswer { QuestionId = q.Id, QuestionOrder = i + 1 }).ToList();
            var attempt = new StudentTestResult { PaperId = p.Id, Answers = answers };
            var result = new TestSubmitResultDto { Questions = answers.Select(a => new TestAnswerReviewDto { StudentAnswerId = a.Id, QuestionOrder = a.QuestionOrder }).ToList() };

            await new PaperContentService(db).EnrichResultAsync(result, attempt);

            Assert.Single(result.Stimuli);
            Assert.Equal(new[] { s.Id }, result.Questions[0].StimulusIds);
            Assert.Equal(new[] { s.Id }, result.Questions[1].StimulusIds);
            Assert.Empty(result.Questions[2].StimulusIds);

            var nonPaper = new StudentTestResult { PaperId = null, Answers = answers };
            var r2 = new TestSubmitResultDto { Questions = result.Questions.Select(q => new TestAnswerReviewDto { StudentAnswerId = q.StudentAnswerId }).ToList() };
            await new PaperContentService(db).EnrichResultAsync(r2, nonPaper);
            Assert.Empty(r2.Stimuli);
        }
    }
}
