using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    public interface IPaperContentService
    {
        // Adds paper instructions + shared stimuli to a Previous-Year-Paper attempt response (additive,
        // optional fields). No-op for every other attempt kind and for papers with no such content, so
        // existing papers/attempts respond exactly as before. Only ACTIVE content is shown to students.
        Task EnrichAsync(TestAttemptStartResponseDto dto, StudentTestResult attempt, CancellationToken ct = default);

        // Same for the submitted-result review (TestSubmitResultDto): stimuli + per-question StimulusIds.
        Task EnrichResultAsync(TestSubmitResultDto dto, StudentTestResult attempt, CancellationToken ct = default);

        // Admin Paper Preview: same instructions/stimuli shape the student gets + questions in number order.
        Task<PaperContentPreviewDto?> GetPreviewAsync(Guid paperId, CancellationToken ct = default);
    }

    public class PaperContentService : IPaperContentService
    {
        private readonly ScoramDbContext _db;
        public PaperContentService(ScoramDbContext db) { _db = db; }

        private async Task<List<StudentPaperInstructionDto>> LoadInstructionsAsync(Guid paperId, CancellationToken ct)
        {
            var rows = await _db.PaperInstructions.AsNoTracking()
                .Where(i => i.PaperId == paperId && i.Status == PaperInstructionStatus.Active)
                .OrderBy(i => i.DisplayOrder).ThenBy(i => i.CreatedAt).ToListAsync(ct);
            return rows.Select(i => new StudentPaperInstructionDto
            {
                Id = i.Id, Title = i.Title, Language = i.Language, DisplayOrder = i.DisplayOrder,
                ContentBlocks = ContentBlocksJsonHelper.Parse(i.ContentBlocksJson)
            }).ToList();
        }

        private async Task<Dictionary<Guid, StudentStimulusDto>> LoadStimuliAsync(IEnumerable<Guid> ids, CancellationToken ct)
        {
            var distinct = ids.Distinct().ToList();
            var result = new Dictionary<Guid, StudentStimulusDto>();
            foreach (var chunk in distinct.Chunk(500))
            {
                var rows = await _db.SharedStimuli.AsNoTracking()
                    .Where(s => chunk.Contains(s.Id) && s.Status == SharedStimulusStatus.Active).ToListAsync(ct);
                foreach (var s in rows)
                    result[s.Id] = new StudentStimulusDto
                    {
                        Id = s.Id, BusinessId = s.BusinessId, Title = s.Title, Type = s.Type, Language = s.Language,
                        ContentBlocks = ContentBlocksJsonHelper.Parse(s.ContentBlocksJson)
                    };
            }
            return result;
        }

        // Shared core: for a Previous-Year-Paper attempt, which ACTIVE stimuli belong to which answer
        // (keyed by StudentAnswer.Id), and the distinct stimuli in first-seen (question) order.
        // Stimulus is read live; the question snapshots themselves are never touched.
        private async Task<(Dictionary<Guid, List<Guid>> PerAnswer, List<StudentStimulusDto> Stimuli)> ResolveAsync(StudentTestResult attempt, CancellationToken ct)
        {
            var empty = (new Dictionary<Guid, List<Guid>>(), new List<StudentStimulusDto>());
            if (attempt.PaperId == null) return empty;
            var paperId = attempt.PaperId.Value;

            var answers = attempt.Answers.OrderBy(a => a.QuestionOrder).ToList();
            var qIds = answers.Where(a => a.QuestionId != null).Select(a => a.QuestionId!.Value).Distinct().ToList();
            var qbIds = answers.Where(a => a.QuestionBankQuestionId != null).Select(a => a.QuestionBankQuestionId!.Value).Distinct().ToList();

            // answer -> stimulus rows, via the paper's own Question or via its Question Bank link row.
            var ownRows = new List<(Guid QuestionId, Guid StimulusId, int Order)>();
            foreach (var chunk in qIds.Chunk(500))
                ownRows.AddRange((await _db.PaperQuestionStimuli.AsNoTracking()
                    .Where(x => x.QuestionId != null && chunk.Contains(x.QuestionId.Value))
                    .Select(x => new { x.QuestionId, x.SharedStimulusId, x.DisplayOrder }).ToListAsync(ct))
                    .Select(x => (x.QuestionId!.Value, x.SharedStimulusId, x.DisplayOrder)));

            var linkRows = new List<(Guid QbQuestionId, Guid StimulusId, int Order)>();
            foreach (var chunk in qbIds.Chunk(500))
                linkRows.AddRange((await _db.PaperQuestionStimuli.AsNoTracking()
                    .Where(x => x.PaperQuestionBankLink != null && x.PaperQuestionBankLink.PaperId == paperId
                        && chunk.Contains(x.PaperQuestionBankLink.QuestionBankQuestionId))
                    .Select(x => new { QbId = x.PaperQuestionBankLink!.QuestionBankQuestionId, x.SharedStimulusId, x.DisplayOrder }).ToListAsync(ct))
                    .Select(x => (x.QbId, x.SharedStimulusId, x.DisplayOrder)));

            if (ownRows.Count == 0 && linkRows.Count == 0) return empty;

            var active = await LoadStimuliAsync(ownRows.Select(r => r.StimulusId).Concat(linkRows.Select(r => r.StimulusId)), ct);
            if (active.Count == 0) return empty;

            var perAnswer = new Dictionary<Guid, List<Guid>>();
            var firstSeen = new List<Guid>();
            foreach (var a in answers)
            {
                var ids = (a.QuestionId != null
                        ? ownRows.Where(r => r.QuestionId == a.QuestionId).Select(r => (r.StimulusId, r.Order))
                        : linkRows.Where(r => r.QbQuestionId == a.QuestionBankQuestionId).Select(r => (r.StimulusId, r.Order)))
                    .Where(r => active.ContainsKey(r.StimulusId)).OrderBy(r => r.Order).Select(r => r.StimulusId).Distinct().ToList();
                if (ids.Count == 0) continue;
                perAnswer[a.Id] = ids;
                foreach (var id in ids) if (!firstSeen.Contains(id)) firstSeen.Add(id);
            }
            return (perAnswer, firstSeen.Select(id => active[id]).ToList());
        }

        public async Task EnrichAsync(TestAttemptStartResponseDto dto, StudentTestResult attempt, CancellationToken ct = default)
        {
            if (attempt.PaperId == null || dto.Questions.Count == 0) return;

            dto.PaperInstructions = await LoadInstructionsAsync(attempt.PaperId.Value, ct);

            var (perAnswer, stimuli) = await ResolveAsync(attempt, ct);
            if (stimuli.Count == 0) return;
            foreach (var q in dto.Questions)
                if (perAnswer.TryGetValue(q.Id, out var ids)) q.StimulusIds = ids;
            dto.Stimuli = stimuli;
        }

        // Result / review screen: same stimuli, attached to the review rows. No-op for non-paper attempts.
        public async Task EnrichResultAsync(TestSubmitResultDto dto, StudentTestResult attempt, CancellationToken ct = default)
        {
            if (attempt.PaperId == null || dto.Questions.Count == 0) return;
            var (perAnswer, stimuli) = await ResolveAsync(attempt, ct);
            if (stimuli.Count == 0) return;
            foreach (var q in dto.Questions)
                if (perAnswer.TryGetValue(q.StudentAnswerId, out var ids)) q.StimulusIds = ids;
            dto.Stimuli = stimuli;
        }

        public async Task<PaperContentPreviewDto?> GetPreviewAsync(Guid paperId, CancellationToken ct = default)
        {
            if (!await _db.Papers.AnyAsync(p => p.Id == paperId, ct)) return null;

            var questions = await _db.Questions.AsNoTracking().Where(q => q.PaperId == paperId).ToListAsync(ct);
            var links = await _db.PaperQuestionBankLinks.AsNoTracking().Include(l => l.QuestionBankQuestion).Where(l => l.PaperId == paperId).ToListAsync(ct);

            var rows = await _db.PaperQuestionStimuli.AsNoTracking()
                .Where(x => (x.Question != null && x.Question.PaperId == paperId) || (x.PaperQuestionBankLink != null && x.PaperQuestionBankLink.PaperId == paperId))
                .Select(x => new { x.QuestionId, x.PaperQuestionBankLinkId, x.SharedStimulusId, x.DisplayOrder }).ToListAsync(ct);
            var stimuli = await LoadStimuliAsync(rows.Select(r => r.SharedStimulusId), ct);

            List<Guid> IdsFor(Guid? qid, Guid? lid) => rows
                .Where(r => (qid != null && r.QuestionId == qid) || (lid != null && r.PaperQuestionBankLinkId == lid))
                .Where(r => stimuli.ContainsKey(r.SharedStimulusId)).OrderBy(r => r.DisplayOrder).Select(r => r.SharedStimulusId).Distinct().ToList();

            var items = questions.Select(q => new PaperContentPreviewQuestionDto
            {
                QuestionNumber = q.QuestionNumber, Source = "Paper", QuestionId = q.Id, QuestionText = q.QuestionText,
                OptionA = q.OptionA, OptionB = q.OptionB, OptionC = q.OptionC, OptionD = q.OptionD,
                QuestionImageUrl = q.QuestionImageUrl, OptionAImageUrl = q.OptionAImageUrl, OptionBImageUrl = q.OptionBImageUrl,
                OptionCImageUrl = q.OptionCImageUrl, OptionDImageUrl = q.OptionDImageUrl,
                ContentBlocks = ContentBlocksJsonHelper.Parse(q.ContentBlocksJson), StimulusIds = IdsFor(q.Id, null)
            }).Concat(links.Where(l => l.QuestionBankQuestion != null).Select(l => new PaperContentPreviewQuestionDto
            {
                QuestionNumber = l.QuestionNumber, Source = "QuestionBank", LinkId = l.Id, QuestionText = l.QuestionBankQuestion!.QuestionText,
                OptionA = l.QuestionBankQuestion.OptionA, OptionB = l.QuestionBankQuestion.OptionB, OptionC = l.QuestionBankQuestion.OptionC, OptionD = l.QuestionBankQuestion.OptionD,
                QuestionImageUrl = l.QuestionBankQuestion.QuestionImageUrl, OptionAImageUrl = l.QuestionBankQuestion.OptionAImageUrl,
                OptionBImageUrl = l.QuestionBankQuestion.OptionBImageUrl, OptionCImageUrl = l.QuestionBankQuestion.OptionCImageUrl,
                OptionDImageUrl = l.QuestionBankQuestion.OptionDImageUrl,
                ContentBlocks = ContentBlocksJsonHelper.Parse(l.QuestionBankQuestion.ContentBlocksJson), StimulusIds = IdsFor(null, l.Id)
            })).OrderBy(i => i.QuestionNumber).ToList();

            var order = new List<Guid>();
            foreach (var q in items) foreach (var id in q.StimulusIds) if (!order.Contains(id)) order.Add(id);

            return new PaperContentPreviewDto
            {
                PaperId = paperId, Instructions = await LoadInstructionsAsync(paperId, ct),
                Stimuli = order.Select(id => stimuli[id]).ToList(), Questions = items
            };
        }
    }
}
