using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    // Thrown for request-shape problems (bad selection, unknown paper...). Message is user-facing.
    public class BulkEditException : Exception
    {
        public BulkEditException(string message) : base(message) { }
    }

    public enum BulkOperationType
    {
        SetFields, SetImage, RemoveImage, IndividualEdits, AttachStimulus, ReplaceStimulus, RemoveStimulus
    }

    public interface IBulkQuestionEditService
    {
        Task<PagedResult<BulkPaperRowDto>> ListPapersAsync(string? search, Guid? examId, int? year, PaperLanguage? language, PaperStatus? status, int page, int pageSize, CancellationToken ct = default);
        Task<PagedResult<BulkQuestionRowDto>> ListQuestionsAsync(IList<Guid> paperIds, BulkQuestionFilterDto filter, int page, int pageSize, CancellationToken ct = default);
        Task<BulkFilterOptionsDto> GetFilterOptionsAsync(IList<Guid> paperIds, CancellationToken ct = default);
        Task<BulkPreviewResultDto> PreviewAsync(BulkRequestDto request, CancellationToken ct = default);
        Task<BulkApplyResultDto> ApplyAsync(BulkRequestDto request, Guid adminId, CancellationToken ct = default);
    }

    // Preview and Apply run the SAME planner (BuildAsync) -- preview just doesn't mutate -- so what the admin
    // confirmed is exactly what is applied. Apply = validate everything first, then ONE SaveChanges (atomic):
    // any validation error means nothing is written. Questions / papers / stimuli are never created or deleted
    // here; only editable question fields and PaperQuestionStimuli relationship rows change.
    public class BulkQuestionEditService : IBulkQuestionEditService
    {
        public const int MaxPapers = 100;
        public const int MaxQuestions = 2000;
        public const int MaxEdits = 200;
        public const int MaxErrorsShown = 50;
        private const int ChunkSize = 500;

        private static readonly string[] ImageFields = { "Question", "OptionA", "OptionB", "OptionC", "OptionD", "Explanation" };

        private readonly ScoramDbContext _db;
        private readonly ILogger<BulkQuestionEditService>? _logger;
        private readonly IQuestionBankMirrorService? _mirror;

        public BulkQuestionEditService(ScoramDbContext db, ILogger<BulkQuestionEditService>? logger = null, IQuestionBankMirrorService? mirror = null)
        { _db = db; _logger = logger; _mirror = mirror; }

        // ================= listing =================
        public async Task<PagedResult<BulkPaperRowDto>> ListPapersAsync(string? search, Guid? examId, int? year, PaperLanguage? language, PaperStatus? status, int page, int pageSize, CancellationToken ct = default)
        {
            page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
            var q = _db.Papers.AsNoTracking().AsQueryable();
            if (examId.HasValue) q = q.Where(p => p.ExamId == examId.Value);
            if (year.HasValue) q = q.Where(p => p.Year == year.Value);
            if (language.HasValue) q = q.Where(p => p.Language == language.Value);
            if (status.HasValue) q = q.Where(p => p.Status == status.Value);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                q = q.Where(p => p.Exam!.Name.Contains(term)
                    || (p.PaperLabel != null && p.PaperLabel.Contains(term))
                    || (p.PaperCode != null && p.PaperCode.Contains(term)));
            }

            var total = await q.CountAsync(ct);
            var rows = await q.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(p => new
                {
                    p.Id, ExamName = p.Exam != null ? p.Exam.Name : "", p.Year, p.Language, p.PaperLabel, p.Status,
                    QuestionCount = p.Questions.Count + p.QuestionBankLinks.Count
                }).ToListAsync(ct);

            return new PagedResult<BulkPaperRowDto>
            {
                TotalCount = total, Page = page, PageSize = pageSize,
                Items = rows.Select(r => new BulkPaperRowDto
                {
                    Id = r.Id, ExamName = r.ExamName, Year = r.Year, Language = r.Language.ToString(), PaperLabel = r.PaperLabel,
                    Status = r.Status.ToString(), QuestionCount = r.QuestionCount, ContentEditable = r.Status != PaperStatus.Published
                }).ToList()
            };
        }

        private static void CheckPaperIds(IList<Guid> paperIds)
        {
            if (paperIds == null || paperIds.Count == 0) throw new BulkEditException("Select at least one paper.");
            if (paperIds.Distinct().Count() > MaxPapers) throw new BulkEditException($"Select at most {MaxPapers} papers at a time.");
        }

        // Both sources of a paper's questions (own Questions + Question Bank links) with the same filters.
        private (IQueryable<Question> questions, IQueryable<PaperQuestionBankLink> links) Filtered(IList<Guid> paperIds, BulkQuestionFilterDto? f)
        {
            var ids = paperIds.Distinct().ToList();
            var qq = _db.Questions.AsNoTracking().Where(q => q.PaperId != null && ids.Contains(q.PaperId.Value));
            var lq = _db.PaperQuestionBankLinks.AsNoTracking().Where(l => ids.Contains(l.PaperId));
            if (f == null) return (qq, lq);

            if (f.PaperId.HasValue) { var pid = f.PaperId.Value; qq = qq.Where(q => q.PaperId == pid); lq = lq.Where(l => l.PaperId == pid); }
            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var term = f.Search.Trim();
                qq = qq.Where(q => q.QuestionText.Contains(term));
                lq = lq.Where(l => l.QuestionBankQuestion!.QuestionText.Contains(term));
            }
            if (!string.IsNullOrWhiteSpace(f.Subject))
            {
                var s = f.Subject.Trim();
                qq = qq.Where(q => q.Subject == s);
                lq = lq.Where(l => l.QuestionBankQuestion!.Subject!.Name == s);
            }
            if (!string.IsNullOrWhiteSpace(f.Topic))
            {
                var t = f.Topic.Trim();
                qq = qq.Where(q => q.Topic == t);
                lq = lq.Where(l => l.QuestionBankQuestion!.Topic!.Name == t);
            }
            if (string.Equals(f.Stimulus, "with", StringComparison.OrdinalIgnoreCase))
            {
                qq = qq.Where(q => _db.PaperQuestionStimuli.Any(s => s.QuestionId == q.Id));
                lq = lq.Where(l => _db.PaperQuestionStimuli.Any(s => s.PaperQuestionBankLinkId == l.Id));
            }
            else if (string.Equals(f.Stimulus, "without", StringComparison.OrdinalIgnoreCase))
            {
                qq = qq.Where(q => !_db.PaperQuestionStimuli.Any(s => s.QuestionId == q.Id));
                lq = lq.Where(l => !_db.PaperQuestionStimuli.Any(s => s.PaperQuestionBankLinkId == l.Id));
            }
            return (qq, lq);
        }

        public async Task<PagedResult<BulkQuestionRowDto>> ListQuestionsAsync(IList<Guid> paperIds, BulkQuestionFilterDto filter, int page, int pageSize, CancellationToken ct = default)
        {
            CheckPaperIds(paperIds);
            page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
            var (qq, lq) = Filtered(paperIds, filter);

            // One UNION ALL over identical projections -> true server-side paging across both sources.
            var rows = qq.Select(q => new BulkQuestionRowDto
            {
                PaperId = q.PaperId!.Value,
                PaperName = q.Paper!.Exam!.Name + " " + q.Paper.Year,
                QuestionNumber = q.QuestionNumber,
                Source = "Paper",
                QuestionId = q.Id,
                LinkId = null,
                TextPreview = q.QuestionText.Length > 160 ? q.QuestionText.Substring(0, 160) : q.QuestionText,
                Subject = q.Subject,
                Topic = q.Topic,
                HasStimulus = _db.PaperQuestionStimuli.Any(s => s.QuestionId == q.Id),
                StimulusBusinessId = _db.PaperQuestionStimuli.Where(s => s.QuestionId == q.Id).OrderBy(s => s.DisplayOrder).Select(s => s.SharedStimulus!.BusinessId).FirstOrDefault(),
                ContentEditable = q.Paper.Status != PaperStatus.Published
            }).Concat(lq.Select(l => new BulkQuestionRowDto
            {
                PaperId = l.PaperId,
                PaperName = l.Paper!.Exam!.Name + " " + l.Paper.Year,
                QuestionNumber = l.QuestionNumber,
                Source = "QuestionBank",
                QuestionId = null,
                LinkId = l.Id,
                TextPreview = l.QuestionBankQuestion!.QuestionText.Length > 160 ? l.QuestionBankQuestion.QuestionText.Substring(0, 160) : l.QuestionBankQuestion.QuestionText,
                Subject = l.QuestionBankQuestion.Subject!.Name,
                Topic = l.QuestionBankQuestion.Topic!.Name,
                HasStimulus = _db.PaperQuestionStimuli.Any(s => s.PaperQuestionBankLinkId == l.Id),
                StimulusBusinessId = _db.PaperQuestionStimuli.Where(s => s.PaperQuestionBankLinkId == l.Id).OrderBy(s => s.DisplayOrder).Select(s => s.SharedStimulus!.BusinessId).FirstOrDefault(),
                ContentEditable = false
            }));

            var total = await rows.CountAsync(ct);
            var items = await rows.OrderBy(r => r.PaperName).ThenBy(r => r.PaperId).ThenBy(r => r.QuestionNumber)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return new PagedResult<BulkQuestionRowDto> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
        }

        public async Task<BulkFilterOptionsDto> GetFilterOptionsAsync(IList<Guid> paperIds, CancellationToken ct = default)
        {
            CheckPaperIds(paperIds);
            var (qq, lq) = Filtered(paperIds, null);
            var s1 = await qq.Select(q => q.Subject).Distinct().Take(300).ToListAsync(ct);
            var s2 = await lq.Select(l => l.QuestionBankQuestion!.Subject!.Name).Distinct().Take(300).ToListAsync(ct);
            var t1 = await qq.Select(q => q.Topic).Distinct().Take(500).ToListAsync(ct);
            var t2 = await lq.Select(l => l.QuestionBankQuestion!.Topic!.Name).Distinct().Take(500).ToListAsync(ct);
            return new BulkFilterOptionsDto
            {
                Subjects = s1.Concat(s2).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x).ToList(),
                Topics = t1.Concat(t2).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x).ToList()
            };
        }

        // ================= planner =================
        private sealed class Item
        {
            public Guid PaperId { get; init; }
            public Paper Paper { get; init; } = null!;
            public int? Number { get; init; }
            public Question? Question { get; init; }
            public Guid? LinkId { get; init; }
            public string Key => Question != null ? "q" + Question.Id : "l" + LinkId;
            public string Label => $"{PaperName(Paper)} Q{(Number?.ToString() ?? "?")}";
        }

        private sealed class Plan
        {
            public List<Item> Items { get; set; } = new();
            public int PaperCount { get; set; }
            public List<string> Errors { get; } = new();
            public List<string> Warnings { get; } = new();
            public List<KeyValuePair<string, int>> Counts { get; } = new();
            public HashSet<string> ChangedKeys { get; } = new();
            public HashSet<Question> ContentChanged { get; } = new();
            public string? StimulusLabel { get; set; }
            public BulkOperationType Type { get; set; }

            public void Bump(string label)
            {
                var i = Counts.FindIndex(c => c.Key == label);
                if (i < 0) Counts.Add(new KeyValuePair<string, int>(label, 1));
                else Counts[i] = new KeyValuePair<string, int>(label, Counts[i].Value + 1);
            }
        }

        private static string PaperName(Paper p)
        {
            var n = $"{p.Exam?.Name} {p.Year}".Trim();
            if (!string.IsNullOrWhiteSpace(p.PaperLabel)) n += " " + p.PaperLabel;
            return n + $" ({p.Language})";
        }

        private static bool TryParseType(string? raw, out BulkOperationType type) =>
            Enum.TryParse(raw, true, out type) && Enum.IsDefined(type);

        private async Task ResolveAsync(BulkRequestDto req, BulkOperationType type, Plan plan, CancellationToken ct)
        {
            var paperIds = (req.PaperIds ?? new()).Distinct().ToList();
            CheckPaperIds(paperIds);
            var papers = await _db.Papers.AsNoTracking().Include(p => p.Exam).Where(p => paperIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            if (papers.Count != paperIds.Count) throw new BulkEditException("Some selected papers no longer exist. Reload and try again.");

            List<StimulusTargetDto> targets;
            if (type == BulkOperationType.IndividualEdits)
            {
                var edits = req.Operation.Edits ?? new();
                if (edits.Count == 0) throw new BulkEditException("There is nothing to save.");
                if (edits.Count > MaxEdits) throw new BulkEditException($"Save at most {MaxEdits} questions at a time.");
                if (edits.Select(e => e.QuestionId).Distinct().Count() != edits.Count) throw new BulkEditException("The same question appears twice in the edits.");
                targets = edits.Select(e => new StimulusTargetDto { QuestionId = e.QuestionId }).ToList();
            }
            else if (req.AllMatching != null)
            {
                if (req.ExpectedCount == null) throw new BulkEditException("Confirm how many questions match before applying to all of them.");
                var (qq, lq) = Filtered(paperIds, req.AllMatching);
                var qIdsAll = await qq.Select(q => q.Id).Take(MaxQuestions + 1).ToListAsync(ct);
                var lIdsAll = await lq.Select(l => l.Id).Take(MaxQuestions + 1).ToListAsync(ct);
                var total = qIdsAll.Count + lIdsAll.Count;
                if (total > MaxQuestions) throw new BulkEditException($"More than {MaxQuestions} questions match. Narrow the filters first.");
                if (total != req.ExpectedCount) throw new BulkEditException($"The matching questions changed ({total} now, {req.ExpectedCount} confirmed). Reload and confirm again.");
                targets = qIdsAll.Select(id => new StimulusTargetDto { QuestionId = id })
                    .Concat(lIdsAll.Select(id => new StimulusTargetDto { LinkId = id })).ToList();
            }
            else targets = req.Targets ?? new();

            if (targets.Count == 0) throw new BulkEditException("Select at least one question.");
            if (targets.Count > MaxQuestions) throw new BulkEditException($"Select at most {MaxQuestions} questions at a time.");
            if (targets.Any(t => (t.QuestionId == null) == (t.LinkId == null)))
                throw new BulkEditException("Each selected question must have exactly one of questionId / linkId.");

            var qIds = targets.Where(t => t.QuestionId != null).Select(t => t.QuestionId!.Value).Distinct().ToList();
            var lIds = targets.Where(t => t.LinkId != null).Select(t => t.LinkId!.Value).Distinct().ToList();

            var questions = new List<Question>();
            foreach (var chunk in qIds.Chunk(ChunkSize))
                questions.AddRange(await _db.Questions.Where(q => chunk.Contains(q.Id) && q.PaperId != null && paperIds.Contains(q.PaperId.Value)).ToListAsync(ct));
            var links = new List<PaperQuestionBankLink>();
            foreach (var chunk in lIds.Chunk(ChunkSize))
                links.AddRange(await _db.PaperQuestionBankLinks.AsNoTracking().Where(l => chunk.Contains(l.Id) && paperIds.Contains(l.PaperId)).ToListAsync(ct));

            if (questions.Count != qIds.Count || links.Count != lIds.Count)
                throw new BulkEditException("Some selected questions don't belong to the selected papers or no longer exist. Reload and try again.");

            plan.Items = questions.Select(q => new Item { PaperId = q.PaperId!.Value, Paper = papers[q.PaperId!.Value], Number = q.QuestionNumber, Question = q })
                .Concat(links.Select(l => new Item { PaperId = l.PaperId, Paper = papers[l.PaperId], Number = l.QuestionNumber, LinkId = l.Id }))
                .OrderBy(i => PaperName(i.Paper)).ThenBy(i => i.Number).ToList();
            plan.PaperCount = plan.Items.Select(i => i.PaperId).Distinct().Count();
        }

        private async Task<Plan> BuildAsync(BulkRequestDto req, bool apply, CancellationToken ct)
        {
            if (req?.Operation == null || !TryParseType(req.Operation.Type, out var type))
                throw new BulkEditException("Choose an operation.");
            var op = req.Operation;
            var plan = new Plan { Type = type };
            await ResolveAsync(req, type, plan, ct);

            switch (type)
            {
                case BulkOperationType.SetFields: PlanSetFields(plan, op, apply); break;
                case BulkOperationType.SetImage:
                case BulkOperationType.RemoveImage: PlanImage(plan, op, type, apply); break;
                case BulkOperationType.IndividualEdits: PlanIndividualEdits(plan, op, apply); break;
                case BulkOperationType.AttachStimulus: await PlanAttachAsync(plan, op, apply, ct); break;
                case BulkOperationType.ReplaceStimulus: await PlanReplaceAsync(plan, op, apply, ct); break;
                case BulkOperationType.RemoveStimulus: await PlanRemoveAsync(plan, op, apply, ct); break;
            }
            return plan;
        }

        // ---------- content-edit helpers ----------
        // Only paper-owned questions of non-published papers. Question Bank rows are canonical and shared
        // across papers, so a bulk content edit would silently change OTHER papers -- never allowed here.
        private static void EnsureContentEditable(Plan plan)
        {
            foreach (var item in plan.Items)
            {
                if (item.Question == null)
                    plan.Errors.Add($"{item.Label}: this is a shared Question Bank question, so content edits would change other papers too. Deselect it (stimulus operations are fine).");
                else if (item.Paper.Status == PaperStatus.Published)
                    plan.Errors.Add($"{item.Label}: the paper is published and locked for content edits (same rule as the question editor). Deselect it.");
            }
        }

        private static void Change<T>(Plan plan, Item item, string label, T current, T next, bool apply, Action set)
        {
            if (EqualityComparer<T>.Default.Equals(current, next)) return;
            plan.Bump(label);
            plan.ChangedKeys.Add(item.Key);
            plan.ContentChanged.Add(item.Question!);
            if (apply) set();
        }

        private static bool TryDifficulty(string? raw, out DifficultyLevel level) =>
            Enum.TryParse(raw?.Trim(), true, out level) && Enum.IsDefined(level);

        private static bool TryOption(string? raw, out OptionLetter letter)
        {
            letter = default;
            var v = raw?.Trim();
            return v is { Length: 1 } && Enum.TryParse(v, true, out letter) && Enum.IsDefined(letter);
        }

        public static bool IsSafeUploadUrl(string? url) =>
            !string.IsNullOrWhiteSpace(url) && url.StartsWith("/uploads/", StringComparison.Ordinal)
            && !url.Contains("..") && !url.Contains('\\') && !url.Contains("://") && !url.Contains('?') && !url.Contains('#');

        private static string? GetImage(Question q, string field) => field switch
        {
            "Question" => q.QuestionImageUrl, "OptionA" => q.OptionAImageUrl, "OptionB" => q.OptionBImageUrl,
            "OptionC" => q.OptionCImageUrl, "OptionD" => q.OptionDImageUrl, _ => q.ExplanationImageUrl
        };

        private static void SetImageValue(Question q, string field, string? url)
        {
            switch (field)
            {
                case "Question": q.QuestionImageUrl = url; break;
                case "OptionA": q.OptionAImageUrl = url; break;
                case "OptionB": q.OptionBImageUrl = url; break;
                case "OptionC": q.OptionCImageUrl = url; break;
                case "OptionD": q.OptionDImageUrl = url; break;
                default: q.ExplanationImageUrl = url; break;
            }
        }

        // ---------- Mode A ----------
        private static void PlanSetFields(Plan plan, BulkOperationDto op, bool apply)
        {
            var f = op.Fields ?? throw new BulkEditException("Choose what to change.");
            if (f.Subject == null && f.Topic == null && f.DifficultyLevel == null && f.Language == null && f.Explanation == null && !f.ClearExplanation)
                throw new BulkEditException("Choose what to change.");
            if (f.Explanation != null && f.ClearExplanation) throw new BulkEditException("Set a new explanation or clear it, not both.");

            string? subject = null, topic = null, language = null, explanation = null;
            DifficultyLevel? difficulty = null;
            if (f.Subject != null) { var v = f.Subject.Trim(); if (v.Length is 0 or > 50) plan.Errors.Add("Subject must be 1-50 characters."); else subject = v; }
            if (f.Topic != null) { var v = f.Topic.Trim(); if (v.Length is 0 or > 100) plan.Errors.Add("Topic must be 1-100 characters."); else topic = v; }
            if (f.Language != null) { var v = f.Language.Trim(); if (v.Length is 0 or > 30) plan.Errors.Add("Language must be 1-30 characters."); else language = v; }
            if (f.DifficultyLevel != null) { if (TryDifficulty(f.DifficultyLevel, out var d)) difficulty = d; else plan.Errors.Add("Difficulty must be Easy, Medium or Hard."); }
            if (f.Explanation != null)
            {
                if (string.IsNullOrWhiteSpace(f.Explanation)) plan.Errors.Add("Explanation can't be empty. Use 'Clear explanation' to remove it.");
                else explanation = f.Explanation;
            }

            EnsureContentEditable(plan);
            if (plan.Errors.Count > 0) return;

            foreach (var item in plan.Items)
            {
                var q = item.Question!;
                if (subject != null) Change(plan, item, "Subject", q.Subject, subject, apply, () => q.Subject = subject);
                if (topic != null) Change(plan, item, "Topic", q.Topic, topic, apply, () => q.Topic = topic);
                if (language != null) Change(plan, item, "Language", q.Language, language, apply, () => q.Language = language);
                if (difficulty != null) Change(plan, item, "Difficulty", q.DifficultyLevel, difficulty.Value, apply, () => q.DifficultyLevel = difficulty.Value);
                if (explanation != null) Change(plan, item, "Explanation", q.Explanation, explanation, apply, () => q.Explanation = explanation);
                if (f.ClearExplanation) Change<string?>(plan, item, "Explanation cleared", q.Explanation, null, apply, () => q.Explanation = null);
            }
            if (f.ClearExplanation) plan.Warnings.Add("The explanation will be removed from the selected questions.");
            plan.Warnings.Add("Edits are also synced to the Question Bank copy of each paper question, like the normal question editor.");
        }

        // ---------- images ----------
        private static void PlanImage(Plan plan, BulkOperationDto op, BulkOperationType type, bool apply)
        {
            var field = ImageFields.FirstOrDefault(f => string.Equals(f, op.ImageField, StringComparison.OrdinalIgnoreCase));
            if (field == null) throw new BulkEditException("Choose which image to change (Question, OptionA-D or Explanation).");
            string? url = null;
            if (type == BulkOperationType.SetImage)
            {
                if (!IsSafeUploadUrl(op.ImageUrl)) plan.Errors.Add("Upload the image first; only uploaded images can be used.");
                else url = op.ImageUrl;
            }
            EnsureContentEditable(plan);
            if (plan.Errors.Count > 0) return;

            var label = type == BulkOperationType.SetImage ? $"{field} image set" : $"{field} image removed";
            foreach (var item in plan.Items)
            {
                var q = item.Question!;
                Change(plan, item, label, GetImage(q, field), url, apply, () => SetImageValue(q, field, url));
            }
            if (type == BulkOperationType.RemoveImage)
                plan.Warnings.Add("Only the image reference is removed. The file stays in storage, because other records may still use it.");
            else
                plan.Warnings.Add("The same uploaded file is referenced by every selected question (no copies). Old files are never deleted by bulk edits.");
        }

        // ---------- Mode B ----------
        private static void PlanIndividualEdits(Plan plan, BulkOperationDto op, bool apply)
        {
            EnsureContentEditable(plan);
            var byId = plan.Items.Where(i => i.Question != null).ToDictionary(i => i.Question!.Id);
            var pending = new List<Action>();

            foreach (var e in op.Edits!)
            {
                if (!byId.TryGetValue(e.QuestionId, out var item)) continue; // already reported (non-editable)
                var q = item.Question!;
                var errorsBefore = plan.Errors.Count;
                string Err(string m) => $"{item.Label}: {m}";

                string? Text(string? v, string name)
                {
                    if (v == null) return null;
                    if (string.IsNullOrWhiteSpace(v)) { plan.Errors.Add(Err($"{name} can't be empty.")); return null; }
                    return v;
                }
                var text = Text(e.QuestionText, "Question text");
                var a = Text(e.OptionA, "Option A"); var b = Text(e.OptionB, "Option B");
                var c = Text(e.OptionC, "Option C"); var d = Text(e.OptionD, "Option D");

                OptionLetter? correct = null;
                if (e.CorrectOption != null) { if (TryOption(e.CorrectOption, out var l)) correct = l; else plan.Errors.Add(Err("the correct option must be A, B, C or D.")); }
                string? subject = null, topic = null, explanation = null;
                if (e.Subject != null) { var v = e.Subject.Trim(); if (v.Length is 0 or > 50) plan.Errors.Add(Err("subject must be 1-50 characters.")); else subject = v; }
                if (e.Topic != null) { var v = e.Topic.Trim(); if (v.Length is 0 or > 100) plan.Errors.Add(Err("topic must be 1-100 characters.")); else topic = v; }
                DifficultyLevel? diff = null;
                if (e.DifficultyLevel != null) { if (TryDifficulty(e.DifficultyLevel, out var dl)) diff = dl; else plan.Errors.Add(Err("difficulty must be Easy, Medium or Hard.")); }
                if (e.Explanation != null && e.ClearExplanation) plan.Errors.Add(Err("set a new explanation or clear it, not both."));
                else if (e.Explanation != null) { if (string.IsNullOrWhiteSpace(e.Explanation)) plan.Errors.Add(Err("explanation can't be empty. Use clear instead.")); else explanation = e.Explanation; }

                string? blocksJson = null; var blocksGiven = e.ContentBlocks != null;
                if (blocksGiven)
                {
                    try { blocksJson = StimulusContentValidator.ValidateAndSerialize(e.ContentBlocks, requireContent: false); }
                    catch (ArgumentException ex) { plan.Errors.Add(Err(ex.Message)); }
                }
                if (plan.Errors.Count != errorsBefore) continue;

                pending.Add(() =>
                {
                    if (text != null) Change(plan, item, "Question text", q.QuestionText, text, apply, () => q.QuestionText = text);
                    if (a != null) Change(plan, item, "Option A", q.OptionA, a, apply, () => q.OptionA = a);
                    if (b != null) Change(plan, item, "Option B", q.OptionB, b, apply, () => q.OptionB = b);
                    if (c != null) Change(plan, item, "Option C", q.OptionC, c, apply, () => q.OptionC = c);
                    if (d != null) Change(plan, item, "Option D", q.OptionD, d, apply, () => q.OptionD = d);
                    if (correct != null) Change(plan, item, "Correct option", q.CorrectOption, correct.Value, apply, () => q.CorrectOption = correct.Value);
                    if (subject != null) Change(plan, item, "Subject", q.Subject, subject, apply, () => q.Subject = subject);
                    if (topic != null) Change(plan, item, "Topic", q.Topic, topic, apply, () => q.Topic = topic);
                    if (diff != null) Change(plan, item, "Difficulty", q.DifficultyLevel, diff.Value, apply, () => q.DifficultyLevel = diff.Value);
                    if (explanation != null) Change(plan, item, "Explanation", q.Explanation, explanation, apply, () => q.Explanation = explanation);
                    if (e.ClearExplanation) Change<string?>(plan, item, "Explanation cleared", q.Explanation, null, apply, () => q.Explanation = null);
                    if (blocksGiven) Change(plan, item, "Content blocks", q.ContentBlocksJson, blocksJson, apply, () => q.ContentBlocksJson = blocksJson);
                });
            }

            if (plan.Errors.Count > 0) return;      // nothing is applied if ANY question is invalid
            foreach (var p in pending) p();
            plan.Warnings.Add("Edits are also synced to the Question Bank copy of each paper question, like the normal question editor.");
        }

        // ---------- stimulus operations (allowed on published papers too: additive repair) ----------
        private static string KeyOf(PaperQuestionStimulus r) => r.QuestionId != null ? "q" + r.QuestionId : "l" + r.PaperQuestionBankLinkId;

        private async Task<List<PaperQuestionStimulus>> LoadRowsAsync(Plan plan, CancellationToken ct)
        {
            var qIds = plan.Items.Where(i => i.Question != null).Select(i => i.Question!.Id).ToList();
            var lIds = plan.Items.Where(i => i.LinkId != null).Select(i => i.LinkId!.Value).ToList();
            var rows = new List<PaperQuestionStimulus>();
            foreach (var chunk in qIds.Chunk(ChunkSize))
                rows.AddRange(await _db.PaperQuestionStimuli.Where(x => x.QuestionId != null && chunk.Contains(x.QuestionId.Value)).ToListAsync(ct));
            foreach (var chunk in lIds.Chunk(ChunkSize))
                rows.AddRange(await _db.PaperQuestionStimuli.Where(x => x.PaperQuestionBankLinkId != null && chunk.Contains(x.PaperQuestionBankLinkId.Value)).ToListAsync(ct));
            return rows;
        }

        private async Task<SharedStimulus?> FindStimulusAsync(Plan plan, Guid? id, bool requireActive, string what, CancellationToken ct)
        {
            if (id == null) { plan.Errors.Add($"Choose the {what} Shared Stimulus."); return null; }
            var s = await _db.SharedStimuli.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s == null) { plan.Errors.Add($"The {what} Shared Stimulus no longer exists."); return null; }
            if (requireActive && s.Status != SharedStimulusStatus.Active)
            { plan.Errors.Add($"{s.BusinessId} is archived. Restore it before attaching."); return null; }
            return s;
        }

        private async Task PlanAttachAsync(Plan plan, BulkOperationDto op, bool apply, CancellationToken ct)
        {
            var s = await FindStimulusAsync(plan, op.StimulusId, true, "", ct);
            if (s == null) return;
            plan.StimulusLabel = $"{s.BusinessId} {s.Title}";
            var rows = await LoadRowsAsync(plan, ct);
            var has = rows.Where(r => r.SharedStimulusId == s.Id).Select(KeyOf).ToHashSet();
            var nextOrder = rows.GroupBy(KeyOf).ToDictionary(g => g.Key, g => g.Max(r => r.DisplayOrder) + 1);

            foreach (var item in plan.Items)
            {
                if (has.Contains(item.Key)) continue;
                plan.Bump("Shared Stimulus attached"); plan.ChangedKeys.Add(item.Key);
                if (apply)
                    _db.PaperQuestionStimuli.Add(new PaperQuestionStimulus
                    {
                        SharedStimulusId = s.Id, QuestionId = item.Question?.Id, PaperQuestionBankLinkId = item.LinkId,
                        DisplayOrder = nextOrder.TryGetValue(item.Key, out var o) ? o : 0, CreatedByAdminId = _adminId
                    });
            }
        }

        private async Task PlanReplaceAsync(Plan plan, BulkOperationDto op, bool apply, CancellationToken ct)
        {
            if (op.FromStimulusId != null && op.FromStimulusId == op.ToStimulusId) { plan.Errors.Add("Choose a different Shared Stimulus to replace with."); return; }
            var from = await FindStimulusAsync(plan, op.FromStimulusId, false, "current", ct);
            var to = await FindStimulusAsync(plan, op.ToStimulusId, true, "new", ct);
            if (from == null || to == null) return;
            plan.StimulusLabel = $"{from.BusinessId} -> {to.BusinessId}";

            var rows = await LoadRowsAsync(plan, ct);
            var hasTo = rows.Where(r => r.SharedStimulusId == to.Id).Select(KeyOf).ToHashSet();
            var itemsByKey = plan.Items.ToDictionary(i => i.Key);
            foreach (var row in rows.Where(r => r.SharedStimulusId == from.Id))
            {
                var key = KeyOf(row);
                plan.Bump("Shared Stimulus replaced"); plan.ChangedKeys.Add(key);
                if (!apply) continue;
                if (hasTo.Contains(key)) _db.PaperQuestionStimuli.Remove(row); else row.SharedStimulusId = to.Id;
            }
            var missing = plan.Items.Count - plan.ChangedKeys.Count;
            if (missing > 0) plan.Warnings.Add($"{missing} selected question(s) don't use {from.BusinessId} and will be left unchanged.");
            plan.Warnings.Add("Neither Shared Stimulus is deleted; only the relationships change.");
        }

        private async Task PlanRemoveAsync(Plan plan, BulkOperationDto op, bool apply, CancellationToken ct)
        {
            SharedStimulus? s = null;
            if (op.StimulusId != null)
            {
                s = await FindStimulusAsync(plan, op.StimulusId, false, "", ct);
                if (s == null) return;
                plan.StimulusLabel = $"{s.BusinessId} {s.Title}";
            }
            else plan.StimulusLabel = "All Shared Stimuli";

            var rows = await LoadRowsAsync(plan, ct);
            foreach (var row in rows.Where(r => s == null || r.SharedStimulusId == s.Id))
            {
                plan.Bump("Shared Stimulus relationship removed"); plan.ChangedKeys.Add(KeyOf(row));
                if (apply) _db.PaperQuestionStimuli.Remove(row);
            }
            plan.Warnings.Add($"This removes the Shared Stimulus relationship from {plan.ChangedKeys.Count} question(s). The questions and the Shared Stimulus content are NOT deleted.");
        }

        private Guid _adminId;

        // ================= public preview / apply =================
        private static BulkPreviewResultDto ToPreview(Plan plan)
        {
            var changed = plan.ChangedKeys.Count;
            var r = new BulkPreviewResultDto
            {
                PaperCount = plan.PaperCount, QuestionCount = plan.Items.Count, ChangedCount = changed,
                UnchangedCount = plan.Items.Count - changed, Operation = plan.Type.ToString(), StimulusLabel = plan.StimulusLabel,
                Changes = plan.Counts.Select(c => new BulkChangeCountDto { Label = c.Key, Count = c.Value }).ToList(),
                Warnings = plan.Warnings.ToList(), ErrorCount = plan.Errors.Count, Errors = plan.Errors.Take(MaxErrorsShown).ToList()
            };
            if (plan.Errors.Count == 0 && changed == 0) r.Warnings.Add("None of the selected questions would change.");
            r.CanApply = plan.Errors.Count == 0 && changed > 0;
            return r;
        }

        public async Task<BulkPreviewResultDto> PreviewAsync(BulkRequestDto request, CancellationToken ct = default)
        {
            var plan = await BuildAsync(request, apply: false, ct);
            _db.ChangeTracker.Clear();
            return ToPreview(plan);
        }

        public async Task<BulkApplyResultDto> ApplyAsync(BulkRequestDto request, Guid adminId, CancellationToken ct = default)
        {
            _adminId = adminId;
            var plan = await BuildAsync(request, apply: true, ct);

            if (plan.Errors.Count > 0)
            {
                _db.ChangeTracker.Clear();
                return new BulkApplyResultDto
                {
                    Success = false, Message = "No changes were applied.", ErrorCount = plan.Errors.Count,
                    Errors = plan.Errors.Take(MaxErrorsShown).ToList()
                };
            }
            if (plan.ChangedKeys.Count == 0)
            {
                _db.ChangeTracker.Clear();
                return new BulkApplyResultDto { Success = true, QuestionsChanged = 0, Message = "Nothing to change: the selected questions already match." };
            }

            try { await _db.SaveChangesAsync(ct); }   // single SaveChanges = one transaction: all or nothing
            catch (DbUpdateException ex)
            {
                _logger?.LogError(ex, "Bulk question edit ({Type}) failed on save; nothing was applied", plan.Type);
                _db.ChangeTracker.Clear();
                return new BulkApplyResultDto
                {
                    Success = false, Message = "No changes were applied. Some questions were changed by someone else. Reload and try again.",
                    ErrorCount = 1, Errors = new() { "A concurrent change blocked the update." }
                };
            }

            // Best-effort mirror sync AFTER the commit -- same behaviour as QuestionsController.Update, never blocks the edit.
            if (_mirror != null && plan.ContentChanged.Count > 0)
            {
                foreach (var q in plan.ContentChanged.Where(q => q.MirroredToQuestionBankQuestionId.HasValue))
                {
                    try { await _mirror.SyncMirrorAsync(_db, q.MirroredToQuestionBankQuestionId!.Value, q); }
                    catch (Exception ex) { _logger?.LogWarning(ex, "Question Bank mirror sync failed for question {QuestionId}", q.Id); }
                }
                try { await _db.SaveChangesAsync(ct); } catch (Exception ex) { _logger?.LogWarning(ex, "Question Bank mirror save failed after bulk edit"); }
            }

            _logger?.LogInformation("Bulk question edit {Type} applied: {Changed}/{Total} questions, {Papers} papers", plan.Type, plan.ChangedKeys.Count, plan.Items.Count, plan.PaperCount);
            return new BulkApplyResultDto { Success = true, QuestionsChanged = plan.ChangedKeys.Count, Message = $"{plan.ChangedKeys.Count} question(s) updated." };
        }
    }
}
