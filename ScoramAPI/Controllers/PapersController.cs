using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // A "Paper" is Exam+Year+Language(+PaperCode) -- see Models/Paper.cs. This controller owns its full
    // lifecycle: Draft (being built) -> PendingReview or Published (on submit, depending on whether the
    // uploading admin has Publish permission) -> Published (after review) or back to Draft (rejected,
    // with a reason).
    [ApiController]
    [Route("api/admin/papers")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class PapersController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IFileStorageService _fileStorage;
        private readonly IInstantSearchService _instantSearch;
        private readonly ILogger<PapersController> _logger;
        private readonly IAuditLogService _audit;

        public PapersController(
            ScoramDbContext db, IAdminPermissionService permissions, IFileStorageService fileStorage,
            IInstantSearchService instantSearch, ILogger<PapersController> logger, IAuditLogService audit)
        {
            _db = db;
            _permissions = permissions;
            _fileStorage = fileStorage;
            _instantSearch = instantSearch;
            _logger = logger;
            _audit = audit;
        }

        // POST /api/admin/papers -- step 2 of the wizard. Returns 409 with the existing paper if this
        // exact Exam+Year+Language+PaperCode combination already exists, so the frontend can redirect
        // into "resume this paper" instead of silently creating a duplicate.
        [HttpPost]
        public async Task<ActionResult<PaperResponseDto>> Create(PaperCreateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper))
                return Forbid();

            var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == dto.ExamId);
            if (exam == null) return BadRequest(new { message = "That exam doesn't exist." });

            // Checked BEFORE this paper is added below, so "does the exam have any other content"
            // genuinely means OTHER content -- see Paper.ExamCreatedForThisPaper's own comment for
            // what this flag is used for later (the bulk-import Undo flow's exam-cleanup offer).
            var examWasEmpty = !await ExamsController.ExamHasContentAsync(_db, dto.ExamId, exam.Name);

            // Free-text fields are trimmed and empty-after-trim is normalized to null *before* the
            // duplicate check -- otherwise "Set A" and "Set A " (an invisible trailing space) would be
            // treated as two different papers, silently defeating the whole point of this check.
            var normalizedPaperCode = string.IsNullOrWhiteSpace(dto.PaperCode) ? null : dto.PaperCode.Trim();
            var normalizedTier = string.IsNullOrWhiteSpace(dto.Tier) ? null : dto.Tier.Trim();
            var normalizedShift = string.IsNullOrWhiteSpace(dto.Shift) ? null : dto.Shift.Trim();
            var normalizedPaperLabel = string.IsNullOrWhiteSpace(dto.PaperLabel) ? null : dto.PaperLabel.Trim();

            // EF Core translates these == comparisons to correct SQL null-handling for the optional
            // fields, so this is a true exact-match check including the "not set" case for every one
            // of them -- a paper's full identity is now Exam+Year+Language+Tier+Date+Shift+Label+Code.
            var existing = await _db.Papers.FirstOrDefaultAsync(p =>
                p.ExamId == dto.ExamId &&
                p.Year == dto.Year &&
                p.Language == dto.Language &&
                p.PaperCode == normalizedPaperCode &&
                p.Tier == normalizedTier &&
                p.ExamDate == dto.ExamDate &&
                p.Shift == normalizedShift &&
                p.PaperLabel == normalizedPaperLabel);

            if (existing != null)
                return Conflict(await ToDto(existing.Id));

            var paper = new Paper
            {
                ExamId = dto.ExamId,
                Year = dto.Year,
                Language = dto.Language,
                PaperCode = normalizedPaperCode,
                Tier = normalizedTier,
                ExamDate = dto.ExamDate,
                Shift = normalizedShift,
                PaperLabel = normalizedPaperLabel,
                Status = PaperStatus.Draft,
                ExamCreatedForThisPaper = examWasEmpty,
                CreatedByAdminId = User.GetAdminId(),
                CreatedAt = DateTime.UtcNow
            };

            _db.Papers.Add(paper);
            await _db.SaveChangesAsync();

            return Ok(await ToDto(paper.Id));
        }

        // GET /api/admin/papers -- the "Uploaded Papers" list. Any admin can view (viewing isn't
        // permission-gated), with optional filters.
        [HttpGet]
        public async Task<ActionResult<PagedResult<PaperResponseDto>>> List(
            [FromQuery] PaperStatus? status, [FromQuery] Guid? examId, [FromQuery] bool mine = false,
            [FromQuery] int? year = null, [FromQuery] PaperLanguage? language = null,
            [FromQuery] string? tier = null, [FromQuery] DateOnly? examDate = null,
            [FromQuery] string? shift = null, [FromQuery] string? paperLabel = null,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var query = _db.Papers.Include(p => p.Exam).Include(p => p.CreatedByAdmin).AsQueryable();

            if (status.HasValue) query = query.Where(p => p.Status == status.Value);
            if (examId.HasValue) query = query.Where(p => p.ExamId == examId.Value);
            if (mine) query = query.Where(p => p.CreatedByAdminId == User.GetAdminId());
            if (year.HasValue) query = query.Where(p => p.Year == year.Value);
            if (language.HasValue) query = query.Where(p => p.Language == language.Value);
            if (tier != null) query = query.Where(p => p.Tier == tier);
            if (examDate.HasValue) query = query.Where(p => p.ExamDate == examDate.Value);
            if (shift != null) query = query.Where(p => p.Shift == shift);
            if (paperLabel != null) query = query.Where(p => p.PaperLabel == paperLabel);

            var totalCount = await query.CountAsync();

            var papers = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new { Paper = p, QuestionCount = p.Questions.Count + p.QuestionBankLinks.Count })
                .ToListAsync();

            return Ok(new PagedResult<PaperResponseDto>
            {
                Items = papers.Select(x => MapToDto(x.Paper, x.QuestionCount)).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        // GET /api/admin/papers/pending -- the review queue. Only meaningful for admins who can act on
        // it, so this requires Publish permission (same as the actual publish/reject actions).
        [HttpGet("pending")]
        public async Task<ActionResult<List<PaperResponseDto>>> ListPending()
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.PublishPaper))
                return Forbid();

            var papers = await _db.Papers
                .Include(p => p.Exam).Include(p => p.CreatedByAdmin)
                .Where(p => p.Status == PaperStatus.PendingReview)
                .OrderBy(p => p.CreatedAt)
                .Select(p => new { Paper = p, QuestionCount = p.Questions.Count + p.QuestionBankLinks.Count })
                .ToListAsync();

            return Ok(papers.Select(x => MapToDto(x.Paper, x.QuestionCount)));
        }

        // GET /api/admin/papers/{id} -- paper detail + its questions in original paper order.
        // Legacy view: only PYQ-upload Questions (Question.PaperId). Use
        // GET /api/admin/papers/{id}/mapped-questions for the FULL merged list including any
        // Question Bank questions mapped onto this paper.
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<object>> GetById(Guid id)
        {
            var paper = await _db.Papers.Include(p => p.Exam).Include(p => p.CreatedByAdmin)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (paper == null) return NotFound(new { message = "Paper not found." });

            var questionEntities = await _db.Questions
                .Include(q => q.Solutions)
                .Include(q => q.Paper).ThenInclude(p => p!.Exam)
                .Where(q => q.PaperId == id)
                .OrderBy(q => q.QuestionNumber)
                .ToListAsync();
            var questions = questionEntities.Select(QuestionsController.MapToDetailDto).ToList();

            var totalCount = questionEntities.Count + await _db.PaperQuestionBankLinks.CountAsync(l => l.PaperId == id);
            return Ok(new { paper = MapToDto(paper, totalCount), questions });
        }

        // GET /api/admin/papers/{id}/mapped-questions -- the FULL question list for this paper,
        // legacy PYQ-upload Questions AND Question-Bank-mapped questions merged and sorted by
        // QuestionNumber, each tagged with its source (spec section 10, "Question Source
        // Transparency"). This is what an admin sees while building/reviewing a Previous Year Paper.
        [HttpGet("{id:guid}/mapped-questions")]
        public async Task<ActionResult<List<PaperMappedQuestionDto>>> GetMappedQuestions(Guid id)
        {
            var paperExists = await _db.Papers.AnyAsync(p => p.Id == id);
            if (!paperExists) return NotFound(new { message = "Paper not found." });

            var pyqRows = await _db.Questions
                .Where(q => q.PaperId == id)
                .Select(q => new PaperMappedQuestionDto
                {
                    QuestionNumber = q.QuestionNumber ?? 0,
                    Source = "PyqUpload",
                    QuestionId = q.Id,
                    LinkId = null,
                    QuestionText = q.QuestionText,
                    Subject = q.Subject,
                    Topic = q.Topic,
                    IsNumberExact = true // PYQ-upload rows always have an admin-typed real Q.No
                })
                .ToListAsync();

            var qbRows = await _db.PaperQuestionBankLinks
                .Include(l => l.QuestionBankQuestion).ThenInclude(q => q!.Subject)
                .Include(l => l.QuestionBankQuestion).ThenInclude(q => q!.Topic)
                .Where(l => l.PaperId == id)
                .Select(l => new PaperMappedQuestionDto
                {
                    QuestionNumber = l.QuestionNumber,
                    Source = "QuestionBank",
                    QuestionId = l.QuestionBankQuestionId,
                    LinkId = l.Id,
                    QuestionText = l.QuestionBankQuestion!.QuestionText,
                    Subject = l.QuestionBankQuestion!.Subject!.Name,
                    Topic = l.QuestionBankQuestion!.Topic!.Name,
                    IsNumberExact = l.IsNumberExact
                })
                .ToListAsync();

            var merged = pyqRows.Concat(qbRows).OrderBy(r => r.QuestionNumber).ToList();
            return Ok(merged);
        }

        // PATCH /api/admin/papers/{id}/config -- sets Duration/Negative marking/Required question
        // count for Previous Year Paper Practice. Allowed any time the paper is editable
        // (Draft/PendingReview) -- same permission as editing its questions.
        [HttpPatch("{id:guid}/config")]
        public async Task<ActionResult<PaperResponseDto>> UpdateConfig(Guid id, PaperConfigUpdateDto dto)
        {
            var canEdit = await _permissions.HasPermissionAsync(User, AdminPermission.EditPaper)
                || await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper);
            if (!canEdit) return Forbid();

            var paper = await _db.Papers.FindAsync(id);
            if (paper == null) return NotFound(new { message = "Paper not found." });
            if (paper.Status == PaperStatus.Published)
                return BadRequest(new { message = "Unpublish this paper before changing its practice settings." });

            paper.DurationMinutes = dto.DurationMinutes;
            paper.NegativeMarkingRatio = dto.NegativeMarkingRatio;
            paper.RequiredQuestionCount = dto.RequiredQuestionCount;
            await _db.SaveChangesAsync();

            return Ok(await ToDto(paper.Id));
        }

        // PATCH /api/admin/papers/{id}/identity -- fixes Exam/Year/Medium/Tier/Date/Shift/Code/Label
        // after creation. Added for the bulk paper-shell import flow (BulkPaperImportController): a
        // row can resolve to the wrong exam (typo that still matched something) or carry a typo'd
        // year/tier/etc, and until now the only fix was deleting the paper and starting over. Same
        // Draft/PendingReview-only restriction and the same exact duplicate-check as Create.
        [HttpPatch("{id:guid}/identity")]
        public async Task<ActionResult<PaperResponseDto>> UpdateIdentity(Guid id, PaperIdentityUpdateDto dto)
        {
            var canEdit = await _permissions.HasPermissionAsync(User, AdminPermission.EditPaper)
                || await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper);
            if (!canEdit) return Forbid();

            var paper = await _db.Papers.FindAsync(id);
            if (paper == null) return NotFound(new { message = "Paper not found." });
            if (paper.Status == PaperStatus.Published)
                return BadRequest(new { message = "Unpublish this paper before changing its identity." });

            var examExists = await _db.Exams.AnyAsync(e => e.Id == dto.ExamId);
            if (!examExists) return BadRequest(new { message = "That exam doesn't exist." });

            var normalizedPaperCode = string.IsNullOrWhiteSpace(dto.PaperCode) ? null : dto.PaperCode.Trim();
            var normalizedTier = string.IsNullOrWhiteSpace(dto.Tier) ? null : dto.Tier.Trim();
            var normalizedShift = string.IsNullOrWhiteSpace(dto.Shift) ? null : dto.Shift.Trim();
            var normalizedPaperLabel = string.IsNullOrWhiteSpace(dto.PaperLabel) ? null : dto.PaperLabel.Trim();

            // Same exact-match check as Create -- excluding this paper itself, since it's always its
            // own identity match otherwise.
            var collision = await _db.Papers.FirstOrDefaultAsync(p =>
                p.Id != id &&
                p.ExamId == dto.ExamId &&
                p.Year == dto.Year &&
                p.Language == dto.Language &&
                p.PaperCode == normalizedPaperCode &&
                p.Tier == normalizedTier &&
                p.ExamDate == dto.ExamDate &&
                p.Shift == normalizedShift &&
                p.PaperLabel == normalizedPaperLabel);

            if (collision != null)
                return Conflict(await ToDto(collision.Id));

            paper.ExamId = dto.ExamId;
            paper.Year = dto.Year;
            paper.Language = dto.Language;
            paper.PaperCode = normalizedPaperCode;
            paper.Tier = normalizedTier;
            paper.ExamDate = dto.ExamDate;
            paper.Shift = normalizedShift;
            paper.PaperLabel = normalizedPaperLabel;
            await _db.SaveChangesAsync();

            return Ok(await ToDto(paper.Id));
        }

        // POST /api/admin/papers/{id}/map-question -- map an EXISTING Question Bank question onto
        // this paper at a given question number (spec section 12: never create a duplicate question
        // just to add it to a paper).
        [HttpPost("{id:guid}/map-question")]
        public async Task<ActionResult<PaperMappedQuestionDto>> MapQuestion(Guid id, PaperQuestionMapDto dto)
        {
            var canEdit = await _permissions.HasPermissionAsync(User, AdminPermission.EditPaper)
                || await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper);
            if (!canEdit) return Forbid();

            var paper = await _db.Papers.FindAsync(id);
            if (paper == null) return NotFound(new { message = "Paper not found." });
            if (paper.Status == PaperStatus.Published)
                return BadRequest(new { message = "Unpublish this paper before changing its questions." });

            var qbQuestion = await _db.QuestionBankQuestions
                .Include(q => q.Subject).Include(q => q.Topic)
                .FirstOrDefaultAsync(q => q.Id == dto.QuestionBankQuestionId);
            if (qbQuestion == null || !qbQuestion.IsActive)
                return BadRequest(new { message = "That Question Bank question doesn't exist or isn't active." });

            // Cross-table uniqueness (spans Question + PaperQuestionBankLink, so it can't be a single
            // DB constraint) -- catches both "this Q.No is already used by a PYQ-upload question" and
            // "this Q.No is already used by another QB mapping" in one check.
            var numberTaken = await _db.Questions.AnyAsync(q => q.PaperId == id && q.QuestionNumber == dto.QuestionNumber)
                || await _db.PaperQuestionBankLinks.AnyAsync(l => l.PaperId == id && l.QuestionNumber == dto.QuestionNumber);
            if (numberTaken)
                return Conflict(new { message = $"Question number {dto.QuestionNumber} is already used in this paper." });

            var alreadyMapped = await _db.PaperQuestionBankLinks
                .AnyAsync(l => l.PaperId == id && l.QuestionBankQuestionId == dto.QuestionBankQuestionId);
            if (alreadyMapped)
                return Conflict(new { message = "This question is already mapped to this paper." });

            var link = new PaperQuestionBankLink
            {
                PaperId = id,
                QuestionBankQuestionId = dto.QuestionBankQuestionId,
                QuestionNumber = dto.QuestionNumber,
                IsNumberExact = true, // admin explicitly chose this Q.No -- it's the real position
                LinkedByAdminId = User.GetAdminId()
            };
            _db.PaperQuestionBankLinks.Add(link);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "Paper.MapQuestion", "Paper", id,
                $"Mapped Question Bank question {dto.QuestionBankQuestionId} as Q.{dto.QuestionNumber}");

            return Ok(new PaperMappedQuestionDto
            {
                QuestionNumber = link.QuestionNumber,
                Source = "QuestionBank",
                QuestionId = qbQuestion.Id,
                LinkId = link.Id,
                QuestionText = qbQuestion.QuestionText,
                Subject = qbQuestion.Subject?.Name ?? "",
                Topic = qbQuestion.Topic?.Name ?? "",
                IsNumberExact = true
            });
        }

        // POST /api/admin/papers/{id}/map-questions-bulk -- add several EXISTING Question Bank
        // questions to this paper at once, e.g. "this paper's Exam+Year already has 40 matching
        // questions sitting in the Question Bank -- add them all" instead of one-by-one. Unlike
        // MapQuestion above, the caller does NOT provide a QuestionNumber per question -- there's no
        // reliable way to know each one's TRUE position in the original paper when adding many at
        // once, so numbers are auto-assigned sequentially right after whatever's already in the paper,
        // and every link created this way is flagged IsNumberExact = false (see
        // StudentPapersController.Start for what that changes about the student's attempt order).
        [HttpPost("{id:guid}/map-questions-bulk")]
        public async Task<ActionResult<PaperBulkMapResultDto>> MapQuestionsBulk(Guid id, PaperQuestionBulkMapDto dto)
        {
            var canEdit = await _permissions.HasPermissionAsync(User, AdminPermission.EditPaper)
                || await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper);
            if (!canEdit) return Forbid();

            var paper = await _db.Papers.FindAsync(id);
            if (paper == null) return NotFound(new { message = "Paper not found." });
            if (paper.Status == PaperStatus.Published)
                return BadRequest(new { message = "Unpublish this paper before changing its questions." });

            var requestedIds = (dto.QuestionBankQuestionIds ?? new List<Guid>()).Distinct().ToList();
            if (requestedIds.Count == 0)
                return BadRequest(new { message = "Select at least one question to add." });
            if (requestedIds.Count > 200)
                return BadRequest(new { message = "Add up to 200 questions at a time." });

            var validIds = await _db.QuestionBankQuestions
                .Where(q => requestedIds.Contains(q.Id) && q.IsActive)
                .Select(q => q.Id)
                .ToListAsync();

            var alreadyMapped = await _db.PaperQuestionBankLinks
                .Where(l => l.PaperId == id && requestedIds.Contains(l.QuestionBankQuestionId))
                .Select(l => l.QuestionBankQuestionId)
                .ToListAsync();

            var toAdd = validIds.Except(alreadyMapped).ToList();
            var invalidIds = requestedIds.Except(validIds).ToList();

            if (toAdd.Count == 0)
            {
                return Ok(new PaperBulkMapResultDto
                {
                    AddedCount = 0,
                    SkippedAlreadyMapped = alreadyMapped,
                    SkippedInvalid = invalidIds,
                    StartQuestionNumber = 0,
                    EndQuestionNumber = 0
                });
            }

            // Next free slot after everything currently in the paper (both sources) -- same
            // "append after whatever's there" idea as MapQuestion's auto-suggested number in the
            // frontend, just computed server-side for N questions at once.
            var maxPyq = await _db.Questions.Where(q => q.PaperId == id)
                .Select(q => (int?)(q.QuestionNumber ?? 0)).MaxAsync() ?? 0;
            var maxQb = await _db.PaperQuestionBankLinks.Where(l => l.PaperId == id)
                .Select(l => (int?)l.QuestionNumber).MaxAsync() ?? 0;
            var nextNumber = Math.Max(maxPyq, maxQb) + 1;

            var adminId = User.GetAdminId();
            var links = toAdd.Select((qId, i) => new PaperQuestionBankLink
            {
                PaperId = id,
                QuestionBankQuestionId = qId,
                QuestionNumber = nextNumber + i,
                IsNumberExact = false,
                LinkedByAdminId = adminId
            }).ToList();

            _db.PaperQuestionBankLinks.AddRange(links);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(adminId, "Paper.MapQuestionsBulk", "Paper", id,
                $"Bulk-mapped {links.Count} Question Bank question(s) as Q.{nextNumber}-Q.{nextNumber + links.Count - 1} (approximate numbering)");

            return Ok(new PaperBulkMapResultDto
            {
                AddedCount = links.Count,
                SkippedAlreadyMapped = alreadyMapped,
                SkippedInvalid = invalidIds,
                StartQuestionNumber = nextNumber,
                EndQuestionNumber = nextNumber + links.Count - 1
            });
        }

        // DELETE /api/admin/papers/{id}/map-question/{linkId} -- unmap a Question Bank question from
        // this paper. Never deletes the underlying QuestionBankQuestion itself (spec section 12,
        // "Remove/unassign it from a paper" -- unassign, not delete).
        [HttpDelete("{id:guid}/map-question/{linkId:guid}")]
        public async Task<IActionResult> UnmapQuestion(Guid id, Guid linkId)
        {
            var canEdit = await _permissions.HasPermissionAsync(User, AdminPermission.EditPaper)
                || await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper);
            if (!canEdit) return Forbid();

            var paper = await _db.Papers.FindAsync(id);
            if (paper == null) return NotFound(new { message = "Paper not found." });
            if (paper.Status == PaperStatus.Published)
                return BadRequest(new { message = "Unpublish this paper before changing its questions." });

            var link = await _db.PaperQuestionBankLinks.FirstOrDefaultAsync(l => l.Id == linkId && l.PaperId == id);
            if (link == null) return NotFound(new { message = "Mapping not found." });

            _db.PaperQuestionBankLinks.Remove(link);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "Paper.UnmapQuestion", "Paper", id, $"Unmapped Q.{link.QuestionNumber}");

            return NoContent();
        }

        // GET /api/admin/papers/{id}/validate -- spec section 14, "Question Number Validation".
        // Missing/duplicate checks only run over the RANGE 1..RequiredQuestionCount when that's set;
        // with no RequiredQuestionCount, integrity isn't enforced and this just reports the raw count.
        [HttpGet("{id:guid}/validate")]
        public async Task<ActionResult<PaperValidationDto>> Validate(Guid id)
        {
            var paper = await _db.Papers.FindAsync(id);
            if (paper == null) return NotFound(new { message = "Paper not found." });

            var pyqNumbers = await _db.Questions.Where(q => q.PaperId == id && q.QuestionNumber != null)
                .Select(q => q.QuestionNumber!.Value).ToListAsync();
            var qbLinks = await _db.PaperQuestionBankLinks.Where(l => l.PaperId == id)
                .Select(l => new { l.QuestionNumber, l.IsNumberExact }).ToListAsync();
            var qbNumbers = qbLinks.Select(l => l.QuestionNumber).ToList();
            var allNumbers = pyqNumbers.Concat(qbNumbers).ToList();
            var hasApproximateNumbers = qbLinks.Any(l => !l.IsNumberExact);

            var duplicates = allNumbers.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).OrderBy(n => n).ToList();

            var required = paper.RequiredQuestionCount;
            var missing = required.HasValue
                ? Enumerable.Range(1, required.Value).Except(allNumbers).OrderBy(n => n).ToList()
                : new List<int>();

            var isReady = required.HasValue && allNumbers.Count >= required.Value && missing.Count == 0 && duplicates.Count == 0;

            string? message = null;
            if (!required.HasValue)
                message = "Set a required question count before this paper can be validated for publishing.";
            else if (missing.Count > 0)
                message = $"{missing.Count} question(s) missing.";
            else if (duplicates.Count > 0)
                message = "Duplicate question numbers found -- fix before publishing.";
            else if (hasApproximateNumbers)
                message = "Ready to publish. Note: some Q.No are auto-assigned (bulk-added), not the exact original position -- students will get a subject-grouped order instead.";

            return Ok(new PaperValidationDto
            {
                RequiredQuestionCount = required ?? 0,
                ActualQuestionCount = allNumbers.Count,
                MissingCount = missing.Count,
                MissingQuestionNumbers = missing,
                DuplicateQuestionNumbers = duplicates,
                IsReadyToPublish = isReady,
                Message = message,
                HasApproximateQuestionNumbers = hasApproximateNumbers
            });
        }

        // PATCH /api/admin/papers/{id}/submit -- "Done" in the wizard. Always queues the paper for
        // review (PendingReview) -- even an admin with Publish permission still needs a separate,
        // explicit Publish click; see the comment further down for why.
        [HttpPatch("{id:guid}/submit")]
        public async Task<ActionResult<PaperResponseDto>> Submit(Guid id)
        {
            var paper = await _db.Papers.FindAsync(id);
            if (paper == null) return NotFound(new { message = "Paper not found." });

            var canUpload = await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper);
            var canEdit = await _permissions.HasPermissionAsync(User, AdminPermission.EditPaper);
            if (!canUpload && !canEdit) return Forbid();

            if (paper.Status != PaperStatus.Draft)
                return BadRequest(new { message = $"This paper is already {paper.Status} -- only a Draft paper can be submitted." });

            var questionCount = await GetCombinedQuestionCountAsync(_db, id);
            if (questionCount == 0)
                return BadRequest(new { message = "Add at least one question before submitting this paper." });

            if (paper.RequiredQuestionCount.HasValue && questionCount < paper.RequiredQuestionCount.Value)
                return BadRequest(new { message = $"This paper needs {paper.RequiredQuestionCount.Value} questions but only has {questionCount}. Add the missing questions before submitting." });

            // Submit never auto-publishes anymore, even for an admin (including a Super Admin, who
            // holds every permission by default -- see AdminPermissionService) who holds PublishPaper.
            // Every paper now goes through PendingReview and needs a separate, explicit Publish click
            // -- this closes the exact gap that let a bad bulk import go straight live in one action
            // with nobody taking a second look at it.
            paper.Status = PaperStatus.PendingReview;
            paper.RejectionReason = null;

            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "Paper.SubmitForReview", "Paper", paper.Id);
            return Ok(await ToDto(paper.Id));
        }

        // PATCH /api/admin/papers/{id}/publish  (Publish permission required)
        [HttpPatch("{id:guid}/publish")]
        public async Task<ActionResult<PaperResponseDto>> Publish(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.PublishPaper))
                return Forbid();

            var paper = await _db.Papers.FindAsync(id);
            if (paper == null) return NotFound(new { message = "Paper not found." });
            if (paper.Status != PaperStatus.PendingReview)
                return BadRequest(new { message = "Only a paper Pending Review can be published." });

            if (paper.RequiredQuestionCount.HasValue)
            {
                var totalCount = await GetCombinedQuestionCountAsync(_db, id);
                if (totalCount < paper.RequiredQuestionCount.Value)
                    return BadRequest(new { message = $"This paper needs {paper.RequiredQuestionCount.Value} questions but only has {totalCount}. It can't be published as incomplete." });
            }

            paper.Status = PaperStatus.Published;
            paper.PublishedAt = DateTime.UtcNow;
            paper.RejectionReason = null;
            await _db.SaveChangesAsync();
            await IndexPaperQuestionsAsync(paper.Id);
            await _audit.LogAsync(User.GetAdminId(), "Paper.Publish", "Paper", paper.Id);

            return Ok(await ToDto(paper.Id));
        }

        // PATCH /api/admin/papers/{id}/reject  (Publish permission required) -- sends it back to Draft
        // with a reason so the uploading admin knows what to fix.
        [HttpPatch("{id:guid}/reject")]
        public async Task<ActionResult<PaperResponseDto>> Reject(Guid id, PaperRejectDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.PublishPaper))
                return Forbid();

            var paper = await _db.Papers.FindAsync(id);
            if (paper == null) return NotFound(new { message = "Paper not found." });
            if (paper.Status != PaperStatus.PendingReview)
                return BadRequest(new { message = "Only a paper Pending Review can be rejected." });

            paper.Status = PaperStatus.Draft;
            paper.RejectionReason = dto.Reason;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "Paper.Reject", "Paper", paper.Id, dto.Reason);

            return Ok(await ToDto(paper.Id));
        }

        // PATCH /api/admin/papers/{id}/unpublish  (Publish permission required) -- a Published paper
        // can't be edited directly; this reopens it for editing by sending it back to PendingReview.
        [HttpPatch("{id:guid}/unpublish")]
        public async Task<ActionResult<PaperResponseDto>> Unpublish(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.PublishPaper))
                return Forbid();

            var paper = await _db.Papers.FindAsync(id);
            if (paper == null) return NotFound(new { message = "Paper not found." });
            if (paper.Status != PaperStatus.Published)
                return BadRequest(new { message = "Only a Published paper can be unpublished." });

            paper.Status = PaperStatus.PendingReview;
            paper.PublishedAt = null;
            await _db.SaveChangesAsync();
            await RemovePaperQuestionsFromIndexAsync(paper.Id);
            await _audit.LogAsync(User.GetAdminId(), "Paper.Unpublish", "Paper", paper.Id);

            return Ok(await ToDto(paper.Id));
        }

        // DELETE /api/admin/papers/{id}  (Delete permission required) -- cascades to every Question
        // under it (see ScoramDbContext), freeing up the Exam+Year+Language(+Code) combination
        // for a fresh upload.
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.DeletePaper))
                return Forbid();

            var paper = await _db.Papers.FindAsync(id);
            if (paper == null) return NotFound(new { message = "Paper not found." });

            var wasPublished = paper.Status == PaperStatus.Published;

            // Best-effort cleanup of any images belonging to this paper's questions before the DB
            // cascade-delete removes the rows themselves (and with them, our only record of the URLs).
            // The SelectMany(q => new[] {...}) form used to live here, but EF Core can't translate an
            // array-literal selector inside SelectMany into SQL -- it always threw at runtime, this was
            // just never exercised until now. Selecting the six columns into a named shape (translatable)
            // and flattening with client-side LINQ after ToListAsync avoids that entirely.
            var questionIds = await _db.Questions.Where(q => q.PaperId == id).Select(q => q.Id).ToListAsync();
            var imageUrlSets = await _db.Questions.Where(q => q.PaperId == id)
                .Select(q => new
                {
                    q.QuestionImageUrl, q.OptionAImageUrl, q.OptionBImageUrl,
                    q.OptionCImageUrl, q.OptionDImageUrl, q.ExplanationImageUrl
                })
                .ToListAsync();
            var imageUrls = imageUrlSets
                .SelectMany(x => new[] { x.QuestionImageUrl, x.OptionAImageUrl, x.OptionBImageUrl, x.OptionCImageUrl, x.OptionDImageUrl, x.ExplanationImageUrl })
                .Where(url => url != null)
                .ToList();

            _db.Papers.Remove(paper);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "Paper.Delete", "Paper", id,
                $"{questionIds.Count} question(s) deleted with it" + (wasPublished ? " (was Published)" : ""));

            foreach (var url in imageUrls) await _fileStorage.DeleteImageAsync(url);
            if (wasPublished) await RemoveQuestionsFromIndexAsync(questionIds);

            return NoContent();
        }

        // ---------- Meilisearch sync helpers ----------
        // A search-index hiccup (Meilisearch down/misconfigured) should never fail the underlying
        // Publish/Unpublish/Delete operation -- these swallow and log instead of throwing.

        private async Task IndexPaperQuestionsAsync(Guid paperId)
        {
            try
            {
                var docs = await _db.Questions
                    .Include(q => q.Paper).ThenInclude(p => p!.Exam)
                    .Where(q => q.PaperId == paperId)
                    .ToListAsync();

                await _instantSearch.IndexQuestionsAsync(docs.Select(ToSearchDocument));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to index paper {PaperId} into the search index", paperId);
            }
        }

        private async Task RemovePaperQuestionsFromIndexAsync(Guid paperId)
        {
            var ids = await _db.Questions.Where(q => q.PaperId == paperId).Select(q => q.Id).ToListAsync();
            await RemoveQuestionsFromIndexAsync(ids);
        }

        private async Task RemoveQuestionsFromIndexAsync(List<Guid> questionIds)
        {
            try
            {
                await _instantSearch.RemoveQuestionsAsync(questionIds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove {Count} question(s) from the search index", questionIds.Count);
            }
        }

        private static QuestionSearchDocument ToSearchDocument(Question q) => QuestionSearchDocument.FromQuestion(q);

        // POST /api/admin/papers/reindex-search  (Super Admin only) -- rebuilds the Meilisearch index
        // from scratch against every currently-Published paper. Needed once for papers that were
        // published before this integration existed; also a safety net if the index and DB ever drift.
        [HttpPost("reindex-search")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> ReindexSearch()
        {
            try
            {
                await _instantSearch.ClearIndexAsync();

                var publishedQuestions = await _db.Questions
                    .Include(q => q.Paper).ThenInclude(p => p!.Exam)
                    .Where(q => q.PaperId != null && q.Paper!.Status == PaperStatus.Published)
                    .ToListAsync();

                await _instantSearch.IndexQuestionsAsync(publishedQuestions.Select(ToSearchDocument));

                return Ok(new { message = $"Reindexed {publishedQuestions.Count} question(s) from Published papers." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reindex the search index");
                return StatusCode(502, new { message = "Couldn't reach the search service. Is Meilisearch running?" });
            }
        }

        private async Task<PaperResponseDto> ToDto(Guid paperId)
        {
            var paper = await _db.Papers.Include(p => p.Exam).Include(p => p.CreatedByAdmin)
                .FirstAsync(p => p.Id == paperId);
            var questionCount = await GetCombinedQuestionCountAsync(_db, paperId);
            return MapToDto(paper, questionCount);
        }

        public static PaperResponseDto MapToDto(Paper p, int? questionCountOverride = null, bool isBookmarked = false, int attemptCount = 0) => new PaperResponseDto
        {
            Id = p.Id,
            ExamId = p.ExamId,
            ExamName = p.Exam?.Name ?? "Unknown",
            ExamLogoUrl = p.Exam?.LogoUrl,
            Year = p.Year,
            Language = p.Language.ToString(),
            PaperCode = p.PaperCode,
            Tier = p.Tier,
            ExamDate = p.ExamDate,
            Shift = p.Shift,
            PaperLabel = p.PaperLabel,
            Status = p.Status.ToString(),
            RejectionReason = p.RejectionReason,
            QuestionCount = questionCountOverride ?? p.Questions?.Count ?? 0,
            CreatedByAdminId = p.CreatedByAdminId,
            CreatedByAdminName = p.CreatedByAdmin?.FullName ?? "Unknown",
            CreatedAt = p.CreatedAt,
            PublishedAt = p.PublishedAt,
            DurationMinutes = p.DurationMinutes,
            NegativeMarkingRatio = p.NegativeMarkingRatio,
            RequiredQuestionCount = p.RequiredQuestionCount,
            IsComplete = !p.RequiredQuestionCount.HasValue
                || (questionCountOverride ?? p.Questions?.Count ?? 0) >= p.RequiredQuestionCount.Value,
            IsConfiguredForPractice = p.DurationMinutes.HasValue && p.NegativeMarkingRatio.HasValue && p.RequiredQuestionCount.HasValue,
            IsBookmarked = isBookmarked,
            AttemptCount = attemptCount
        };

        // Combined question count for a paper: legacy PYQ-upload Questions (Question.PaperId) PLUS
        // Question Bank questions mapped onto it (PaperQuestionBankLink) -- see Models/Paper.cs.
        // Used everywhere a Paper's QuestionCount is reported, so students/admins always see the
        // TRUE total instead of just the PYQ-upload half of it.
        internal static async Task<int> GetCombinedQuestionCountAsync(ScoramDbContext db, Guid paperId)
        {
            var pyqCount = await db.Questions.CountAsync(q => q.PaperId == paperId);
            var qbCount = await db.PaperQuestionBankLinks.CountAsync(l => l.PaperId == paperId);
            return pyqCount + qbCount;
        }
    }
}
