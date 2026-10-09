using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    public class StimulusLinkException : Exception
    {
        public int StatusCode { get; }
        public StimulusLinkException(string message, int statusCode = 400) : base(message) { StatusCode = statusCode; }
    }

    public interface IStimulusLinkService
    {
        Task<StimulusLinkOperationResultDto> AttachAsync(Guid paperId, Guid stimulusId, IList<StimulusTargetDto> targets, Guid adminId, CancellationToken ct = default);
        Task<StimulusLinkOperationResultDto> DetachAsync(Guid paperId, Guid stimulusId, IList<StimulusTargetDto> targets, CancellationToken ct = default);
        Task<StimulusLinkOperationResultDto> ReplaceAsync(Guid paperId, Guid fromStimulusId, Guid toStimulusId, IList<StimulusTargetDto> targets, CancellationToken ct = default);
        Task<List<PaperStimulusLinkDto>> ListForPaperAsync(Guid paperId, CancellationToken ct = default);
    }

    // Only ever touches PaperQuestionStimuli rows. Questions, papers and stimuli are never modified or
    // deleted here. Every operation is validate-everything-first, then ONE SaveChanges (atomic).
    public class StimulusLinkService : IStimulusLinkService
    {
        public const int MaxTargets = 1000;
        private readonly ScoramDbContext _db;
        public StimulusLinkService(ScoramDbContext db) { _db = db; }

        private sealed record Resolved(List<Guid> QuestionIds, List<Guid> LinkIds);

        private async Task<Resolved> ResolveAsync(Guid paperId, IList<StimulusTargetDto> targets, CancellationToken ct)
        {
            if (targets == null || targets.Count == 0) throw new StimulusLinkException("Select at least one question.");
            if (targets.Count > MaxTargets) throw new StimulusLinkException($"Too many questions in one request (max {MaxTargets}).");

            foreach (var t in targets)
                if ((t.QuestionId == null) == (t.LinkId == null))
                    throw new StimulusLinkException("Each selected question must have exactly one of questionId / linkId.");

            var qIds = targets.Where(t => t.QuestionId != null).Select(t => t.QuestionId!.Value).Distinct().ToList();
            var lIds = targets.Where(t => t.LinkId != null).Select(t => t.LinkId!.Value).Distinct().ToList();

            if (qIds.Count > 0)
            {
                var ok = await _db.Questions.Where(q => qIds.Contains(q.Id) && q.PaperId == paperId).CountAsync(ct);
                if (ok != qIds.Count) throw new StimulusLinkException("Some selected questions do not belong to this paper.");
            }
            if (lIds.Count > 0)
            {
                var ok = await _db.PaperQuestionBankLinks.Where(l => lIds.Contains(l.Id) && l.PaperId == paperId).CountAsync(ct);
                if (ok != lIds.Count) throw new StimulusLinkException("Some selected questions do not belong to this paper.");
            }
            return new Resolved(qIds, lIds);
        }

        private IQueryable<PaperQuestionStimulus> RowsFor(Resolved r) =>
            _db.PaperQuestionStimuli.Where(x =>
                (x.QuestionId != null && r.QuestionIds.Contains(x.QuestionId.Value)) ||
                (x.PaperQuestionBankLinkId != null && r.LinkIds.Contains(x.PaperQuestionBankLinkId.Value)));

        private static string Key(Guid? q, Guid? l) => q != null ? "q" + q : "l" + l;

        private async Task<SharedStimulus> GetStimulusAsync(Guid id, bool requireActive, CancellationToken ct)
        {
            var s = await _db.SharedStimuli.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new StimulusLinkException("Shared Stimulus not found.", 404);
            if (requireActive && s.Status != SharedStimulusStatus.Active)
                throw new StimulusLinkException("This Shared Stimulus is archived. Restore it before attaching.");
            return s;
        }

        private async Task SaveAsync(CancellationToken ct)
        {
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // e.g. another admin attached the same stimulus a moment ago (unique index) -- nothing partial is saved.
                throw new StimulusLinkException("These questions were changed by someone else. Refresh and try again. No changes were applied.", 409);
            }
        }

        public async Task<StimulusLinkOperationResultDto> AttachAsync(Guid paperId, Guid stimulusId, IList<StimulusTargetDto> targets, Guid adminId, CancellationToken ct = default)
        {
            await GetStimulusAsync(stimulusId, requireActive: true, ct);
            var r = await ResolveAsync(paperId, targets, ct);

            var existing = await RowsFor(r).AsNoTracking()
                .Select(x => new { x.QuestionId, x.PaperQuestionBankLinkId, x.SharedStimulusId, x.DisplayOrder }).ToListAsync(ct);

            var linked = existing.Where(e => e.SharedStimulusId == stimulusId).Select(e => Key(e.QuestionId, e.PaperQuestionBankLinkId)).ToHashSet();
            var nextOrder = existing.GroupBy(e => Key(e.QuestionId, e.PaperQuestionBankLinkId)).ToDictionary(g => g.Key, g => g.Max(e => e.DisplayOrder) + 1);

            int created = 0, skipped = 0;
            foreach (var t in targets.DistinctBy(t => Key(t.QuestionId, t.LinkId)))
            {
                var key = Key(t.QuestionId, t.LinkId);
                if (linked.Contains(key)) { skipped++; continue; }
                _db.PaperQuestionStimuli.Add(new PaperQuestionStimulus
                {
                    SharedStimulusId = stimulusId,
                    QuestionId = t.QuestionId,
                    PaperQuestionBankLinkId = t.LinkId,
                    DisplayOrder = nextOrder.TryGetValue(key, out var o) ? o : 0,
                    CreatedByAdminId = adminId
                });
                created++;
            }
            if (created > 0) await SaveAsync(ct);
            return new StimulusLinkOperationResultDto { Changed = created, Skipped = skipped, Message = $"{created} question(s) linked, {skipped} already linked." };
        }

        public async Task<StimulusLinkOperationResultDto> DetachAsync(Guid paperId, Guid stimulusId, IList<StimulusTargetDto> targets, CancellationToken ct = default)
        {
            var r = await ResolveAsync(paperId, targets, ct);
            var rows = await RowsFor(r).Where(x => x.SharedStimulusId == stimulusId).ToListAsync(ct);
            var requested = targets.DistinctBy(t => Key(t.QuestionId, t.LinkId)).Count();
            if (rows.Count > 0)
            {
                _db.PaperQuestionStimuli.RemoveRange(rows); // relationship rows only
                await SaveAsync(ct);
            }
            return new StimulusLinkOperationResultDto { Changed = rows.Count, Skipped = requested - rows.Count, Message = $"Removed the Shared Stimulus from {rows.Count} question(s). Questions and the stimulus were not deleted." };
        }

        public async Task<StimulusLinkOperationResultDto> ReplaceAsync(Guid paperId, Guid fromStimulusId, Guid toStimulusId, IList<StimulusTargetDto> targets, CancellationToken ct = default)
        {
            if (fromStimulusId == toStimulusId) throw new StimulusLinkException("Choose a different Shared Stimulus to replace with.");
            await GetStimulusAsync(toStimulusId, requireActive: true, ct);
            var r = await ResolveAsync(paperId, targets, ct);

            var rows = await RowsFor(r).Where(x => x.SharedStimulusId == fromStimulusId || x.SharedStimulusId == toStimulusId).ToListAsync(ct);
            var hasTo = rows.Where(x => x.SharedStimulusId == toStimulusId).Select(x => Key(x.QuestionId, x.PaperQuestionBankLinkId)).ToHashSet();

            int changed = 0;
            foreach (var row in rows.Where(x => x.SharedStimulusId == fromStimulusId))
            {
                if (hasTo.Contains(Key(row.QuestionId, row.PaperQuestionBankLinkId))) _db.PaperQuestionStimuli.Remove(row); // already has target: just drop old link
                else row.SharedStimulusId = toStimulusId;
                changed++;
            }
            var requested = targets.DistinctBy(t => Key(t.QuestionId, t.LinkId)).Count();
            if (changed > 0) await SaveAsync(ct);
            return new StimulusLinkOperationResultDto { Changed = changed, Skipped = requested - changed, Message = $"{changed} relationship(s) changed. No stimulus or question was deleted." };
        }

        public async Task<List<PaperStimulusLinkDto>> ListForPaperAsync(Guid paperId, CancellationToken ct = default)
        {
            // Two single-query projections (no N+1): paper's own questions, and its QB links.
            var fromQuestions = await _db.PaperQuestionStimuli.AsNoTracking()
                .Where(x => x.Question != null && x.Question.PaperId == paperId)
                .Select(x => new PaperStimulusLinkDto
                {
                    QuestionId = x.QuestionId, QuestionNumber = x.Question!.QuestionNumber,
                    StimulusId = x.SharedStimulusId, StimulusBusinessId = x.SharedStimulus!.BusinessId,
                    StimulusTitle = x.SharedStimulus.Title, StimulusStatus = x.SharedStimulus.Status.ToString(), DisplayOrder = x.DisplayOrder
                }).ToListAsync(ct);

            var fromLinks = await _db.PaperQuestionStimuli.AsNoTracking()
                .Where(x => x.PaperQuestionBankLink != null && x.PaperQuestionBankLink.PaperId == paperId)
                .Select(x => new PaperStimulusLinkDto
                {
                    LinkId = x.PaperQuestionBankLinkId, QuestionNumber = x.PaperQuestionBankLink!.QuestionNumber,
                    StimulusId = x.SharedStimulusId, StimulusBusinessId = x.SharedStimulus!.BusinessId,
                    StimulusTitle = x.SharedStimulus.Title, StimulusStatus = x.SharedStimulus.Status.ToString(), DisplayOrder = x.DisplayOrder
                }).ToListAsync(ct);

            return fromQuestions.Concat(fromLinks).OrderBy(x => x.QuestionNumber).ThenBy(x => x.DisplayOrder).ToList();
        }
    }
}
