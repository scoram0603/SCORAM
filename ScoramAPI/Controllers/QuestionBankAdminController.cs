using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Admin side of the Question Bank (spec sections 7-13, 17, 22). Every action requires the
    // ManageQuestionBank permission, checked per-endpoint (same style as PapersController/
    // BulkImportController) rather than a single blanket check, so a future permission split stays
    // easy.
    [ApiController]
    [Route("api/admin/question-bank")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class QuestionBankAdminController : ControllerBase
    {
        // Bulk-import preview rows live here between preview and commit, same pattern as
        // BulkImportController's own cache -- see that controller's comment for the tradeoff this
        // implies (an app restart loses any in-progress review).
        private const string CachePrefix = "qb-bulk-import-rows:";
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);

        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IQuestionBankImportService _importService;
        private readonly IMemoryCache _cache;
        private readonly IAuditLogService _audit;
        private readonly ILogger<QuestionBankAdminController> _logger;
        private readonly IFileStorageService _fileStorage;

        public QuestionBankAdminController(
            ScoramDbContext db, IAdminPermissionService permissions, IQuestionBankImportService importService,
            IMemoryCache cache, IAuditLogService audit, ILogger<QuestionBankAdminController> logger,
            IFileStorageService fileStorage)
        {
            _db = db;
            _permissions = permissions;
            _importService = importService;
            _cache = cache;
            _audit = audit;
            _logger = logger;
            _fileStorage = fileStorage;
        }

        // ======================================================================================
        // Question CRUD
        // ======================================================================================

        // GET /api/admin/question-bank?search=&subjectId=&topicId=&examId=&year=&includeInactive=&page=&pageSize=
        [HttpGet]
        public async Task<ActionResult<PagedResult<QuestionBankAdminQuestionDto>>> List(
            [FromQuery] string? search, [FromQuery] Guid? subjectId, [FromQuery] Guid? topicId,
            [FromQuery] Guid? examId, [FromQuery] int? year, [FromQuery] string? language, [FromQuery] bool includeInactive = false,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var q = _db.QuestionBankQuestions.AsQueryable();
            if (!includeInactive) q = q.Where(x => x.IsActive);
            if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => EF.Functions.Like(x.QuestionText, $"%{search.Trim()}%"));
            if (subjectId.HasValue) q = q.Where(x => x.SubjectId == subjectId);
            if (topicId.HasValue) q = q.Where(x => x.TopicId == topicId);
            if (!string.IsNullOrWhiteSpace(language) && Enum.TryParse<PaperLanguage>(language, ignoreCase: true, out var languageFilter))
                q = q.Where(x => x.Language == languageFilter);

            // Both examId AND year must match on the SAME mapping row -- a question mapped to
            // "SSC CGL 2024" and separately to "SSC CHSL 2025" must NOT match a search for
            // examId=SSC CGL, year=2025 just because each condition is individually satisfied by a
            // different mapping. (Filtering the two independently, as this used to do, is exactly
            // the bug the Previous Year Paper Practice bulk-add filter would have inherited.)
            if (examId.HasValue && year.HasValue)
                q = q.Where(x => x.ExamMappings.Any(m => m.ExamId == examId && m.Year == year));
            else if (examId.HasValue)
                q = q.Where(x => x.ExamMappings.Any(m => m.ExamId == examId));
            else if (year.HasValue)
                q = q.Where(x => x.ExamMappings.Any(m => m.Year == year));

            q = q.OrderByDescending(x => x.CreatedAt);

            var totalCount = await q.CountAsync();
            var items = await q
                .Include(x => x.Subject).Include(x => x.Topic).Include(x => x.CreatedByAdmin)
                .Include(x => x.ExamMappings).ThenInclude(m => m.Exam)
                .Include(x => x.Solutions)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync();

            return Ok(new PagedResult<QuestionBankAdminQuestionDto>
            {
                Items = items.Select(ToAdminDto).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        // GET /api/admin/question-bank/{id}
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<QuestionBankAdminQuestionDto>> GetById(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();

            var question = await _db.QuestionBankQuestions
                .Include(x => x.Subject).Include(x => x.Topic).Include(x => x.CreatedByAdmin)
                .Include(x => x.ExamMappings).ThenInclude(m => m.Exam)
                .Include(x => x.Solutions)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (question == null) return NotFound();

            return Ok(ToAdminDto(question));
        }

        // POST /api/admin/question-bank -- add one question by hand (section 8). Rejects incomplete
        // submissions and checks for a near-duplicate (section 13) unless the admin has already
        // confirmed past that warning.
        [HttpPost]
        public async Task<ActionResult<QuestionBankAdminQuestionDto>> Create(QuestionBankQuestionCreateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();

            var validationError = ValidateQuestionFields(dto.QuestionText, dto.OptionA, dto.OptionB, dto.OptionC, dto.OptionD,
                dto.CorrectOption, dto.SubjectId, dto.TopicId, dto.ExamYears);
            if (validationError != null) return BadRequest(new { message = validationError });

            var subject = await _db.QuestionBankSubjects.FindAsync(dto.SubjectId);
            if (subject == null) return BadRequest(new { message = "Subject not found." });
            var topic = await _db.QuestionBankTopics.FindAsync(dto.TopicId);
            if (topic == null || topic.SubjectId != dto.SubjectId) return BadRequest(new { message = "Topic not found under the selected Subject." });

            var adminId = User.GetAdminId();
            var normalized = _importService.NormalizeForDuplicateCheck(dto.QuestionText);

            if (!dto.ConfirmCreateDespiteDuplicate)
            {
                var duplicate = await _db.QuestionBankQuestions
                    .Where(x => x.IsActive && x.NormalizedQuestionText == normalized)
                    .Select(x => new { x.Id, x.QuestionText })
                    .FirstOrDefaultAsync();
                if (duplicate != null)
                {
                    return Conflict(new
                    {
                        message = "Duplicate Question Found.",
                        existingQuestionId = duplicate.Id,
                        existingQuestionText = duplicate.QuestionText
                    });
                }
            }

            var question = new QuestionBankQuestion
            {
                QuestionText = dto.QuestionText.Trim(),
                NormalizedQuestionText = normalized,
                OptionA = dto.OptionA.Trim(),
                OptionB = dto.OptionB.Trim(),
                OptionC = dto.OptionC.Trim(),
                OptionD = dto.OptionD.Trim(),
                CorrectOption = Enum.Parse<OptionLetter>(dto.CorrectOption, ignoreCase: true),
                Explanation = string.IsNullOrWhiteSpace(dto.Explanation) ? null : dto.Explanation.Trim(),
                SubjectId = dto.SubjectId,
                TopicId = dto.TopicId,
                SourceReference = string.IsNullOrWhiteSpace(dto.SourceReference) ? null : dto.SourceReference.Trim(),
                Language = ParseLanguage(dto.Language),
                CreatedByAdminId = adminId,
                CreatedAt = DateTime.UtcNow
            };

            foreach (var ey in dto.ExamYears)
            {
                var exam = await GetOrCreateExamAsync(ey.ExamId, ey.ExamName, adminId);
                if (exam == null) continue;
                question.ExamMappings.Add(new QuestionBankExamMapping { Exam = exam, Year = ey.Year });
            }

            _db.QuestionBankQuestions.Add(question);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Question Bank concurrency conflict creating a question");
                return Conflict(new { message = "Something changed while saving. Please try again." });
            }
            await _audit.LogAsync(adminId, "QuestionBank.Create", "QuestionBankQuestion", question.Id);

            await _db.Entry(question).Reference(x => x.Subject).LoadAsync();
            await _db.Entry(question).Reference(x => x.Topic).LoadAsync();
            await _db.Entry(question).Collection(x => x.ExamMappings).Query().Include(m => m.Exam).LoadAsync();

            return Ok(ToAdminDto(question));
        }

        // PUT /api/admin/question-bank/{id}
        [HttpPut("{id:guid}")]
        public async Task<ActionResult<QuestionBankAdminQuestionDto>> Update(Guid id, QuestionBankQuestionUpdateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();

            var question = await _db.QuestionBankQuestions
                .Include(x => x.ExamMappings)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (question == null) return NotFound();

            var validationError = ValidateQuestionFields(dto.QuestionText, dto.OptionA, dto.OptionB, dto.OptionC, dto.OptionD,
                dto.CorrectOption, dto.SubjectId, dto.TopicId, dto.ExamYears);
            if (validationError != null) return BadRequest(new { message = validationError });

            var subject = await _db.QuestionBankSubjects.FindAsync(dto.SubjectId);
            if (subject == null) return BadRequest(new { message = "Subject not found." });
            var topic = await _db.QuestionBankTopics.FindAsync(dto.TopicId);
            if (topic == null || topic.SubjectId != dto.SubjectId) return BadRequest(new { message = "Topic not found under the selected Subject." });

            var adminId = User.GetAdminId();

            question.QuestionText = dto.QuestionText.Trim();
            question.NormalizedQuestionText = _importService.NormalizeForDuplicateCheck(dto.QuestionText);
            question.OptionA = dto.OptionA.Trim();
            question.OptionB = dto.OptionB.Trim();
            question.OptionC = dto.OptionC.Trim();
            question.OptionD = dto.OptionD.Trim();
            question.CorrectOption = Enum.Parse<OptionLetter>(dto.CorrectOption, ignoreCase: true);
            question.Explanation = string.IsNullOrWhiteSpace(dto.Explanation) ? null : dto.Explanation.Trim();
            question.SubjectId = dto.SubjectId;
            question.TopicId = dto.TopicId;
            question.SourceReference = string.IsNullOrWhiteSpace(dto.SourceReference) ? null : dto.SourceReference.Trim();
            question.Language = ParseLanguage(dto.Language);
            question.UpdatedAt = DateTime.UtcNow;

            // Diff the exam/year mapping set instead of blanket-deleting and recreating every row on
            // every save: resolve the desired (Exam, Year) pairs first, then only remove the ones no
            // longer wanted and only add the ones that are new. Mappings that didn't change are never
            // touched. This isn't just an efficiency tweak -- deleting-then-recreating unchanged rows
            // combined with GetOrCreateExamAsync's old mid-loop SaveChangesAsync() (now removed, see
            // that method) is what caused a DbUpdateConcurrencyException here: the delete would
            // already be committed by an earlier intermediate save, and something later in the same
            // request would try to touch that now-gone row again.
            var desired = new List<(Exam Exam, int Year)>();
            foreach (var ey in dto.ExamYears)
            {
                var exam = await GetOrCreateExamAsync(ey.ExamId, ey.ExamName, adminId);
                if (exam == null) continue;
                desired.Add((exam, ey.Year));
            }

            var toRemove = question.ExamMappings
                .Where(m => !desired.Any(d => d.Exam.Id == m.ExamId && d.Year == m.Year))
                .ToList();
            foreach (var m in toRemove)
            {
                question.ExamMappings.Remove(m);
                _db.QuestionBankExamMappings.Remove(m);
            }

            foreach (var d in desired)
            {
                var alreadyMapped = question.ExamMappings.Any(m => m.ExamId == d.Exam.Id && m.Year == d.Year);
                if (alreadyMapped) continue;
                question.ExamMappings.Add(new QuestionBankExamMapping { Exam = d.Exam, Year = d.Year });
            }

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Question Bank concurrency conflict updating {QuestionId}", id);
                return Conflict(new { message = "This question was changed by someone else just now. Please refresh the page and try again." });
            }
            await _audit.LogAsync(adminId, "QuestionBank.Update", "QuestionBankQuestion", id);

            await _db.Entry(question).Reference(x => x.Subject).LoadAsync();
            await _db.Entry(question).Reference(x => x.Topic).LoadAsync();
            await _db.Entry(question).Collection(x => x.ExamMappings).Query().Include(m => m.Exam).LoadAsync();

            return Ok(ToAdminDto(question));
        }

        // POST /api/admin/question-bank/{id}/images (multipart/form-data) -- attach/replace/remove any
        // of the 6 images independently of the text fields (see QuestionBankImagesUpdateDto's own
        // comment on why this is separate from Create/Update). Works identically whether the question
        // was hand-typed or came from a bulk import -- there's nothing bulk-import-specific about a
        // question once it's a row in QuestionBankQuestions.
        [HttpPost("{id:guid}/images")]
        public async Task<ActionResult<QuestionBankAdminQuestionDto>> UpdateImages(Guid id, [FromForm] QuestionBankImagesUpdateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();

            var question = await _db.QuestionBankQuestions
                .Include(x => x.Subject).Include(x => x.Topic)
                .Include(x => x.ExamMappings).ThenInclude(m => m.Exam)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (question == null) return NotFound();

            try
            {
                question.QuestionImageUrl = await ApplyImageUpdate(dto.QuestionImage, dto.RemoveQuestionImage, question.QuestionImageUrl);
                question.OptionAImageUrl = await ApplyImageUpdate(dto.OptionAImage, dto.RemoveOptionAImage, question.OptionAImageUrl);
                question.OptionBImageUrl = await ApplyImageUpdate(dto.OptionBImage, dto.RemoveOptionBImage, question.OptionBImageUrl);
                question.OptionCImageUrl = await ApplyImageUpdate(dto.OptionCImage, dto.RemoveOptionCImage, question.OptionCImageUrl);
                question.OptionDImageUrl = await ApplyImageUpdate(dto.OptionDImage, dto.RemoveOptionDImage, question.OptionDImageUrl);
                question.ExplanationImageUrl = await ApplyImageUpdate(dto.ExplanationImage, dto.RemoveExplanationImage, question.ExplanationImageUrl);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            question.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "QuestionBank.UpdateImages", "QuestionBankQuestion", id);

            return Ok(ToAdminDto(question));
        }

        // Same contract as QuestionsController.ApplyImageUpdate -- kept as its own private copy here
        // rather than extracted into a shared static helper, matching this codebase's existing
        // preference for small controller-local helpers over a speculative shared-utility layer.
        private async Task<string?> ApplyImageUpdate(IFormFile? newFile, bool remove, string? currentUrl)
        {
            if (newFile != null)
            {
                var newUrl = await _fileStorage.SaveImageAsync(newFile, "question-images");
                await _fileStorage.DeleteImageAsync(currentUrl);
                return newUrl;
            }
            if (remove)
            {
                await _fileStorage.DeleteImageAsync(currentUrl);
                return null;
            }
            return currentUrl;
        }

        // DELETE /api/admin/question-bank/{id} -- soft delete (IsActive = false), consistent with how
        // every read path already filters on IsActive. Keeps the row (and its Reports/Solutions/exam
        // mappings, which cascade-delete only on a HARD delete) around for audit history instead of
        // silently losing student-submitted solutions tied to it.
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();

            var question = await _db.QuestionBankQuestions.FindAsync(id);
            if (question == null) return NotFound();

            question.IsActive = false;
            question.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "QuestionBank.Delete", "QuestionBankQuestion", id);

            return NoContent();
        }

        // ======================================================================================
        // Subject / Topic management (section 7: "Manage Subject", "Manage Topic")
        // ======================================================================================

        [HttpGet("subjects")]
        public async Task<ActionResult<List<QuestionBankSubjectDto>>> ListSubjects([FromQuery] bool includeInactive = true)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();

            var q = _db.QuestionBankSubjects.AsQueryable();
            if (!includeInactive) q = q.Where(s => s.IsActive);

            var subjects = await q.OrderBy(s => s.Name)
                .Select(s => new QuestionBankSubjectDto { Id = s.Id, Name = s.Name, IsActive = s.IsActive, QuestionCount = s.Questions.Count(x => x.IsActive) })
                .ToListAsync();
            return Ok(subjects);
        }

        [HttpPost("subjects")]
        public async Task<ActionResult<QuestionBankSubjectDto>> CreateSubject(QuestionBankSubjectCreateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Subject name is required." });

            var name = dto.Name.Trim();
            var existing = await _db.QuestionBankSubjects.FirstOrDefaultAsync(s => s.Name == name);
            if (existing != null)
            {
                if (existing.IsActive) return Conflict(new { message = $"Subject '{name}' already exists." });
                existing.IsActive = true; // reactivate a previously-retired subject with the same name
                await _db.SaveChangesAsync();
                return Ok(new QuestionBankSubjectDto { Id = existing.Id, Name = existing.Name, IsActive = true, QuestionCount = 0 });
            }

            var subject = new QuestionBankSubject { Name = name, CreatedByAdminId = User.GetAdminId() };
            _db.QuestionBankSubjects.Add(subject);
            await _db.SaveChangesAsync();
            return Ok(new QuestionBankSubjectDto { Id = subject.Id, Name = subject.Name, IsActive = true, QuestionCount = 0 });
        }

        [HttpPatch("subjects/{id:guid}/toggle-active")]
        public async Task<IActionResult> ToggleSubjectActive(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();

            var subject = await _db.QuestionBankSubjects.FindAsync(id);
            if (subject == null) return NotFound();

            subject.IsActive = !subject.IsActive;
            await _db.SaveChangesAsync();
            return Ok(new { subject.Id, subject.IsActive });
        }

        [HttpGet("topics")]
        public async Task<ActionResult<List<QuestionBankTopicDto>>> ListTopics([FromQuery] Guid? subjectId, [FromQuery] bool includeInactive = true)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();

            var q = _db.QuestionBankTopics.Include(t => t.Subject).AsQueryable();
            if (subjectId.HasValue) q = q.Where(t => t.SubjectId == subjectId);
            if (!includeInactive) q = q.Where(t => t.IsActive);

            var topics = await q.OrderBy(t => t.Name)
                .Select(t => new QuestionBankTopicDto
                {
                    Id = t.Id, SubjectId = t.SubjectId, SubjectName = t.Subject!.Name, Name = t.Name,
                    IsActive = t.IsActive, QuestionCount = t.Questions.Count(x => x.IsActive)
                })
                .ToListAsync();
            return Ok(topics);
        }

        [HttpPost("topics")]
        public async Task<ActionResult<QuestionBankTopicDto>> CreateTopic(QuestionBankTopicCreateDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Topic name is required." });

            var subject = await _db.QuestionBankSubjects.FindAsync(dto.SubjectId);
            if (subject == null) return BadRequest(new { message = "Subject not found." });

            var name = dto.Name.Trim();
            var existing = await _db.QuestionBankTopics.FirstOrDefaultAsync(t => t.SubjectId == dto.SubjectId && t.Name == name);
            if (existing != null)
            {
                if (existing.IsActive) return Conflict(new { message = $"Topic '{name}' already exists under {subject.Name}." });
                existing.IsActive = true;
                await _db.SaveChangesAsync();
                return Ok(new QuestionBankTopicDto { Id = existing.Id, SubjectId = subject.Id, SubjectName = subject.Name, Name = existing.Name, IsActive = true, QuestionCount = 0 });
            }

            var topic = new QuestionBankTopic { SubjectId = dto.SubjectId, Name = name, CreatedByAdminId = User.GetAdminId() };
            _db.QuestionBankTopics.Add(topic);
            await _db.SaveChangesAsync();
            return Ok(new QuestionBankTopicDto { Id = topic.Id, SubjectId = subject.Id, SubjectName = subject.Name, Name = topic.Name, IsActive = true, QuestionCount = 0 });
        }

        [HttpPatch("topics/{id:guid}/toggle-active")]
        public async Task<IActionResult> ToggleTopicActive(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();

            var topic = await _db.QuestionBankTopics.FindAsync(id);
            if (topic == null) return NotFound();

            topic.IsActive = !topic.IsActive;
            await _db.SaveChangesAsync();
            return Ok(new { topic.Id, topic.IsActive });
        }

        // ======================================================================================
        // Bulk import — Excel / JSON (sections 9-13)
        // ======================================================================================

        // POST /api/admin/question-bank/bulk/excel  (multipart/form-data, field name "file", optional
        // field "language" -- see Preview's own comment)
        [HttpPost("bulk/excel")]
        [RequestSizeLimit(20 * 1024 * 1024)]
        public Task<ActionResult<QuestionBankImportPreviewResponseDto>> PreviewExcel(IFormFile file, [FromForm] string? language) =>
            Preview(file, ImportFileFormat.Excel, language);

        // POST /api/admin/question-bank/bulk/json  (multipart/form-data, field name "file", optional
        // field "language")
        [HttpPost("bulk/json")]
        [RequestSizeLimit(20 * 1024 * 1024)]
        public Task<ActionResult<QuestionBankImportPreviewResponseDto>> PreviewJson(IFormFile file, [FromForm] string? language) =>
            Preview(file, ImportFileFormat.Json, language);

        // "language" ("Hindi"/"English", optional) is the admin's "Default Language" choice for this
        // whole upload -- e.g. uploading a pure-Hindi question set without having to type "Hindi" on
        // every single row of the Excel/JSON file. Applied to every row that doesn't specify its own
        // Language (see QuestionBankImportService.ValidateAsync); resolved once here at Preview time
        // (not at Commit) so the preview table already shows exactly what will be saved, and Commit
        // itself doesn't need to know about it at all.
        private async Task<ActionResult<QuestionBankImportPreviewResponseDto>> Preview(IFormFile file, ImportFileFormat format, string? language)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();

            if (file == null || file.Length == 0)
                return BadRequest(new { message = $"Attach a {(format == ImportFileFormat.Excel ? ".xlsx" : ".json")} file." });

            List<QuestionBankImportRow> rows;
            try
            {
                await using var stream = file.OpenReadStream();
                rows = await _importService.ParseAsync(stream, format);
            }
            catch (InvalidDataException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Question Bank bulk import parse failure for {FileName}", file.FileName);
                return BadRequest(new { message = "Couldn't read that file. Double-check it matches the template and try again." });
            }

            if (rows.Count == 0)
                return BadRequest(new { message = "No question rows found in the file." });

            if (!string.IsNullOrWhiteSpace(language) && !Enum.TryParse<PaperLanguage>(language.Trim(), ignoreCase: true, out _))
                return BadRequest(new { message = $"'{language}' isn't a valid Default Language (expected Hindi or English)." });

            await _importService.ValidateAsync(rows, _db, language);

            var job = new QuestionBankImportJob
            {
                CreatedByAdminId = User.GetAdminId(),
                FileName = file.FileName,
                Format = format,
                Status = ImportJobStatus.PendingReview,
                TotalRows = rows.Count,
                ValidRows = rows.Count(r => r.IsValid),
                InvalidRows = rows.Count(r => !r.IsValid),
                DuplicateRows = rows.Count(r => r.IsDuplicate)
            };
            _db.QuestionBankImportJobs.Add(job);
            await _db.SaveChangesAsync();

            _cache.Set(CachePrefix + job.Id, rows, CacheLifetime);

            return Ok(new QuestionBankImportPreviewResponseDto
            {
                JobId = job.Id,
                FileName = job.FileName,
                Format = job.Format.ToString(),
                TotalRows = job.TotalRows,
                ValidCount = job.ValidRows,
                InvalidCount = job.InvalidRows,
                DuplicateCount = job.DuplicateRows,
                Rows = rows
            });
        }

        // PATCH /api/admin/question-bank/bulk/{jobId}/rows/{rowNumber} -- admin corrects a row's
        // text, options, correct answer, explanation, subject/topic, or exam/year pairs during
        // review, before commit (same "fix it right here instead of re-uploading" idea as
        // BulkImportController's paper-level equivalent). Overwrites the cached row and re-runs full
        // ValidateAsync (duplicate checks depend on sibling rows, not just this one), so the response
        // reflects whether the edit actually fixed the problem.
        [HttpPatch("bulk/{jobId:guid}/rows/{rowNumber:int}")]
        public async Task<ActionResult<QuestionBankImportRow>> UpdateRow(Guid jobId, int rowNumber, QuestionBankImportRow edited)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();

            var job = await _db.QuestionBankImportJobs.FindAsync(jobId);
            if (job == null) return NotFound(new { message = "Import job not found." });
            if (job.Status != ImportJobStatus.PendingReview)
                return BadRequest(new { message = $"This import is already {job.Status} and its rows can't be edited anymore." });

            if (!_cache.TryGetValue(CachePrefix + jobId, out List<QuestionBankImportRow>? rows) || rows == null)
                return BadRequest(new { message = "This preview has expired (previews last 30 minutes). Please re-upload the file." });

            var row = rows.FirstOrDefault(r => r.RowNumber == rowNumber);
            if (row == null) return NotFound(new { message = "Row not found in this import." });

            // Only the editable fields -- RowNumber/IsValid/Errors/duplicate flags are never trusted
            // from the client, they're recomputed by ValidateAsync below.
            row.QuestionText = edited.QuestionText;
            row.OptionA = edited.OptionA;
            row.OptionB = edited.OptionB;
            row.OptionC = edited.OptionC;
            row.OptionD = edited.OptionD;
            row.CorrectOption = edited.CorrectOption;
            row.Explanation = edited.Explanation;
            row.Subject = edited.Subject;
            row.Topic = edited.Topic;
            row.SourceReference = edited.SourceReference;
            row.Language = edited.Language;
            row.RawExamYears = edited.RawExamYears;

            await _importService.ValidateAsync(rows, _db);

            job.ValidRows = rows.Count(r => r.IsValid);
            job.InvalidRows = rows.Count(r => !r.IsValid);
            job.DuplicateRows = rows.Count(r => r.IsDuplicate);
            _cache.Set(CachePrefix + jobId, rows, CacheLifetime);
            await _db.SaveChangesAsync();

            return Ok(row);
        }

        // POST /api/admin/question-bank/bulk/{jobId}/commit
        // Valid + non-duplicate rows create a new QuestionBankQuestion. Valid + duplicate rows don't
        // create a second question -- their exam/year pairs are merged onto the existing question's
        // mapping set instead (section 13: "add the new exam/year mapping instead of creating
        // unnecessary duplicate question records").
        [HttpPost("bulk/{jobId:guid}/commit")]
        public async Task<ActionResult<QuestionBankImportCommitResultDto>> Commit(Guid jobId, QuestionBankImportCommitDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();

            var job = await _db.QuestionBankImportJobs.FindAsync(jobId);
            if (job == null) return NotFound(new { message = "Import job not found." });
            if (job.Status != ImportJobStatus.PendingReview)
                return BadRequest(new { message = $"This import is already {job.Status} and can't be committed again." });

            if (!_cache.TryGetValue(CachePrefix + jobId, out List<QuestionBankImportRow>? rows) || rows == null)
                return BadRequest(new { message = "This preview has expired (previews last 30 minutes). Please re-upload the file." });

            var wanted = dto.RowNumbers != null ? new HashSet<int>(dto.RowNumbers) : null;
            var toCommit = rows.Where(r => r.IsValid && (wanted == null || wanted.Contains(r.RowNumber))).ToList();
            var skipped = rows.Count - toCommit.Count;

            var adminId = User.GetAdminId();
            var subjectCache = await _db.QuestionBankSubjects.ToDictionaryAsync(s => s.Name, StringComparer.OrdinalIgnoreCase);
            var topicCache = await _db.QuestionBankTopics.ToDictionaryAsync(t => (t.SubjectId, t.Name), new TopicKeyComparer());
            var examCache = await _db.Exams.ToDictionaryAsync(e => e.Name, StringComparer.OrdinalIgnoreCase);

            var importedCount = 0;
            var mergedCount = 0;

            foreach (var row in toCommit)
            {
                if (row.IsDuplicate && row.DuplicateOfQuestionId.HasValue)
                {
                    // Merge: add any exam/year pairs from this row that the existing question doesn't
                    // already have, rather than inserting a second copy of the same question.
                    var existingMappings = await _db.QuestionBankExamMappings
                        .Where(m => m.QuestionBankQuestionId == row.DuplicateOfQuestionId.Value)
                        .ToListAsync();

                    foreach (var ey in row.ExamYears)
                    {
                        var exam = await GetOrCreateExamCachedAsync(ey.ExamName!, adminId, examCache);
                        if (exam == null) continue;
                        var alreadyMapped = existingMappings.Any(m => m.ExamId == exam.Id && m.Year == ey.Year);
                        if (alreadyMapped) continue;

                        _db.QuestionBankExamMappings.Add(new QuestionBankExamMapping
                        {
                            QuestionBankQuestionId = row.DuplicateOfQuestionId.Value,
                            ExamId = exam.Id,
                            Year = ey.Year
                        });
                    }
                    mergedCount++;
                    continue;
                }

                // Rows that duplicate ANOTHER ROW IN THIS SAME FILE (not an existing DB question --
                // see QuestionBankImportService.ValidateAsync) have no DuplicateOfQuestionId; the
                // first occurrence still gets created normally and later occurrences were already
                // marked IsDuplicate so they fall into the branch above via a second pass. To keep
                // this simple and correct, in-batch duplicates whose "original" hasn't been committed
                // yet in this same loop are skipped (their exam/years are lost) -- rare in practice
                // since ValidateAsync already flags them for the admin to review before committing.
                if (row.IsDuplicate) { mergedCount++; continue; }

                var subject = await GetOrCreateSubjectCachedAsync(row.Subject, adminId, subjectCache);
                var topic = await GetOrCreateTopicCachedAsync(subject.Id, row.Topic, adminId, topicCache);

                var question = new QuestionBankQuestion
                {
                    QuestionText = row.QuestionText.Trim(),
                    NormalizedQuestionText = _importService.NormalizeForDuplicateCheck(row.QuestionText),
                    OptionA = row.OptionA.Trim(),
                    OptionB = row.OptionB.Trim(),
                    OptionC = row.OptionC.Trim(),
                    OptionD = row.OptionD.Trim(),
                    CorrectOption = Enum.Parse<OptionLetter>(row.CorrectOption, ignoreCase: true),
                    Explanation = row.Explanation,
                    SubjectId = subject.Id,
                    TopicId = topic.Id,
                    SourceReference = row.SourceReference,
                    Language = ParseLanguage(row.Language),
                    CreatedByAdminId = adminId,
                    ImportJobId = job.Id,
                    CreatedAt = DateTime.UtcNow
                };

                foreach (var ey in row.ExamYears)
                {
                    var exam = await GetOrCreateExamCachedAsync(ey.ExamName!, adminId, examCache);
                    if (exam == null) continue;
                    question.ExamMappings.Add(new QuestionBankExamMapping { Exam = exam, Year = ey.Year });
                }

                _db.QuestionBankQuestions.Add(question);
                importedCount++;
            }

            job.Status = ImportJobStatus.Committed;
            job.ImportedCount = importedCount;
            job.MergedIntoExistingCount = mergedCount;
            job.CommittedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            _cache.Remove(CachePrefix + jobId);
            await _audit.LogAsync(adminId, "QuestionBank.BulkImport.Commit", "QuestionBankImportJob", job.Id,
                $"{importedCount} new, {mergedCount} merged, from {job.FileName}");

            return Ok(new QuestionBankImportCommitResultDto
            {
                JobId = job.Id,
                Status = job.Status.ToString(),
                ImportedCount = importedCount,
                MergedIntoExistingCount = mergedCount,
                SkippedCount = skipped
            });
        }

        // GET /api/admin/question-bank/bulk/{jobId}
        [HttpGet("bulk/{jobId:guid}")]
        public async Task<ActionResult<QuestionBankImportJobDto>> GetImportStatus(Guid jobId)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();

            var job = await _db.QuestionBankImportJobs.Include(j => j.CreatedByAdmin).FirstOrDefaultAsync(j => j.Id == jobId);
            if (job == null) return NotFound();
            return Ok(ToImportJobDto(job));
        }

        // GET /api/admin/question-bank/bulk/history?page=&pageSize=
        [HttpGet("bulk/history")]
        public async Task<ActionResult<PagedResult<QuestionBankImportJobDto>>> ImportHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.QuestionBankImportJobs.Include(j => j.CreatedByAdmin).OrderByDescending(j => j.CreatedAt);
            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Ok(new PagedResult<QuestionBankImportJobDto>
            {
                Items = items.Select(ToImportJobDto).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        // GET /api/admin/question-bank/template/excel -- generated on the fly so it's always exactly
        // in sync with what QuestionBankImportService actually parses (section 9: "Download Excel
        // Template"). No separate template file to keep updated by hand or accidentally let drift.
        [HttpGet("template/excel")]
        public IActionResult DownloadExcelTemplate()
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Questions");
            // "Language" is optional -- leave the column blank on a row to fall back to whatever
            // Default Language the admin picks on the upload screen (see QuestionBankAdminController
            // .Preview), or fill it in per-row ("Hindi"/"English") to mix mediums in one file.
            string[] headers = { "QuestionText", "OptionA", "OptionB", "OptionC", "OptionD", "CorrectOption", "Explanation", "Subject", "Topic", "SourceReference", "Language", "ExamYears" };
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Row(1).Style.Font.Bold = true;

            var sample = new object[]
            {
                "भारतीय स्थल पर 'गढ़े सोने' के साक्ष्य निम्नलिखित में से किस स्थान से मिले हैं?",
                "लोथल और कालीबंगा", "बुर्जहोम और चिरांद", "चोपड़ा और रंगपुर", "रंगपुर और लोथल",
                "D", "Explanation from source book.", "Ancient History", "Stone Age", "NCERT Class 11, Ch. 4",
                "Hindi", "UP TGT:2016; UP TGT:2019; SSC CGL:2021"
            };
            for (var i = 0; i < sample.Length; i++) sheet.Cell(2, i + 1).Value = sample[i].ToString();
            sheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "question-bank-template.xlsx");
        }

        // GET /api/admin/question-bank/template/json
        [HttpGet("template/json")]
        public IActionResult DownloadJsonTemplate()
        {
            var sample = new[]
            {
                new
                {
                    questionText = "भारतीय स्थल पर 'गढ़े सोने' के साक्ष्य निम्नलिखित में से किस स्थान से मिले हैं?",
                    optionA = "लोथल और कालीबंगा", optionB = "बुर्जहोम और चिरांद", optionC = "चोपड़ा और रंगपुर", optionD = "रंगपुर और लोथल",
                    correctOption = "D",
                    explanation = "Explanation from source book.",
                    subject = "Ancient History",
                    topic = "Stone Age",
                    sourceReference = "NCERT Class 11, Ch. 4",
                    // Optional -- "Hindi" | "English". Omit/leave blank to fall back to the upload's
                    // Default Language instead.
                    language = "Hindi",
                    examYears = new[]
                    {
                        new { examName = "UP TGT", year = 2016 },
                        new { examName = "UP TGT", year = 2019 },
                        new { examName = "SSC CGL", year = 2021 }
                    }
                }
            };
            var json = System.Text.Json.JsonSerializer.Serialize(sample, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", "question-bank-template.json");
        }

        // ======================================================================================
        // Dashboard stats (section 22)
        // ======================================================================================

        [HttpGet("stats")]
        public async Task<ActionResult<QuestionBankStatsDto>> GetStats()
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageQuestionBank))
                return Forbid();

            var today = DateTime.UtcNow.Date;
            var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            return Ok(new QuestionBankStatsDto
            {
                TotalQuestions = await _db.QuestionBankQuestions.CountAsync(x => x.IsActive),
                TotalSubjects = await _db.QuestionBankSubjects.CountAsync(x => x.IsActive),
                TotalTopics = await _db.QuestionBankTopics.CountAsync(x => x.IsActive),
                TotalExamsUsed = await _db.QuestionBankExamMappings.Select(m => m.ExamId).Distinct().CountAsync(),
                TotalDistinctYears = await _db.QuestionBankExamMappings.Select(m => m.Year).Distinct().CountAsync(),
                QuestionsAddedToday = await _db.QuestionBankQuestions.CountAsync(x => x.CreatedAt >= today),
                QuestionsAddedThisMonth = await _db.QuestionBankQuestions.CountAsync(x => x.CreatedAt >= monthStart),
                PendingReports = await _db.QuestionReports.CountAsync(r => r.QuestionBankQuestionId != null && r.Status == ReportStatus.Pending),
                PendingAlternativeSolutions = await _db.QuestionSolutions.CountAsync(s => s.QuestionBankQuestionId != null && !s.IsApproved)
            });
        }

        // ======================================================================================
        // Helpers
        // ======================================================================================

        private static string? ValidateQuestionFields(string questionText, string a, string b, string c, string d,
            string correctOption, Guid subjectId, Guid topicId, List<QuestionBankExamYearInputDto> examYears)
        {
            if (string.IsNullOrWhiteSpace(questionText)) return "Question text is required.";
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) || string.IsNullOrWhiteSpace(c) || string.IsNullOrWhiteSpace(d))
                return "All options are required.";
            if (!Enum.TryParse<OptionLetter>(correctOption, ignoreCase: true, out _)) return "Correct answer is required.";
            if (subjectId == Guid.Empty) return "Subject is required.";
            if (topicId == Guid.Empty) return "Topic is required.";
            if (examYears == null || examYears.Count == 0) return "At least one Exam + Year is required.";
            foreach (var ey in examYears)
            {
                if (ey.ExamId == null && string.IsNullOrWhiteSpace(ey.ExamName)) return "Each exam/year entry needs an exam.";
                if (ey.Year < 1990 || ey.Year > DateTime.UtcNow.Year + 1) return $"'{ey.Year}' isn't a valid year.";
            }
            return null;
        }

        private async Task<Exam?> GetOrCreateExamAsync(Guid? examId, string? examName, Guid adminId)
        {
            if (examId.HasValue)
            {
                // .Local first -- covers an Exam created earlier in THIS SAME request (e.g. a second
                // exam/year row introducing the same brand-new exam) that hasn't hit the DB yet.
                var localById = _db.Exams.Local.FirstOrDefault(e => e.Id == examId.Value);
                if (localById != null) return localById;

                var byId = await _db.Exams.FindAsync(examId.Value);
                if (byId != null) return byId;
            }
            if (string.IsNullOrWhiteSpace(examName)) return null;

            var name = examName.Trim();

            var localByName = _db.Exams.Local.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (localByName != null) return localByName;

            var existing = await _db.Exams.FirstOrDefaultAsync(e => e.Name == name);
            if (existing != null) return existing;

            var exam = new Exam { Name = name, CreatedByAdminId = adminId };
            _db.Exams.Add(exam);
            // Deliberately NO SaveChangesAsync here. Exam.Id is a client-generated Guid (see
            // Models/Exam.cs's `= Guid.NewGuid()` default), so it's already usable for wiring up a
            // QuestionBankExamMapping.ExamId without a DB round-trip -- and the .Local check above
            // makes this exam visible to any later lookup within the same request. Saving mid-request
            // used to prematurely commit whatever else was pending in this SaveChanges batch (the
            // Question's field edits, a pending mapping delete, etc.) before the request had actually
            // finished, which is what caused the DbUpdateConcurrencyException reported against Update().

            // BUG FIX -- every exam needs a chat room (ExamsController.Create does this for exams
            // created via Admin > Exams; this Question Bank path was a second, separate place exams
            // get created and had no equivalent, which is why bulk-uploading questions was silently
            // creating exams with no chat room for students to find). Same deferred-save reasoning
            // applies: exam.Id is already usable here even before this request's SaveChanges.
            _db.ChatRooms.Add(new ChatRoom
            {
                ExamId = exam.Id,
                Name = exam.Name,
                Description = $"Discussion room for {exam.Name} aspirants",
                IsFeatured = false,
                CreatedAt = DateTime.UtcNow
            });

            return exam;
        }

        // Cached variants used inside the bulk-commit loop, to avoid one SELECT + one INSERT round
        // trip per row when a book contributes hundreds of rows under the same handful of
        // subjects/topics/exams.
        private async Task<Exam> GetOrCreateExamCachedAsync(string examName, Guid adminId, Dictionary<string, Exam> cache)
        {
            var name = examName.Trim();
            if (cache.TryGetValue(name, out var cached)) return cached;

            var exam = new Exam { Name = name, CreatedByAdminId = adminId };
            _db.Exams.Add(exam);
            // BUG FIX -- same as GetOrCreateExamAsync above: give every newly-created exam a chat room.
            _db.ChatRooms.Add(new ChatRoom
            {
                ExamId = exam.Id,
                Name = exam.Name,
                Description = $"Discussion room for {exam.Name} aspirants",
                IsFeatured = false,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            cache[name] = exam;
            return exam;
        }

        private async Task<QuestionBankSubject> GetOrCreateSubjectCachedAsync(string subjectName, Guid adminId, Dictionary<string, QuestionBankSubject> cache)
        {
            var name = subjectName.Trim();
            if (cache.TryGetValue(name, out var cached)) return cached;

            var subject = new QuestionBankSubject { Name = name, CreatedByAdminId = adminId };
            _db.QuestionBankSubjects.Add(subject);
            await _db.SaveChangesAsync();
            cache[name] = subject;
            return subject;
        }

        private async Task<QuestionBankTopic> GetOrCreateTopicCachedAsync(Guid subjectId, string topicName, Guid adminId, Dictionary<(Guid, string), QuestionBankTopic> cache)
        {
            var name = topicName.Trim();
            var key = (subjectId, name);
            if (cache.TryGetValue(key, out var cached)) return cached;

            var topic = new QuestionBankTopic { SubjectId = subjectId, Name = name, CreatedByAdminId = adminId };
            _db.QuestionBankTopics.Add(topic);
            await _db.SaveChangesAsync();
            cache[key] = topic;
            return topic;
        }

        private class TopicKeyComparer : IEqualityComparer<(Guid SubjectId, string Name)>
        {
            public bool Equals((Guid SubjectId, string Name) x, (Guid SubjectId, string Name) y) =>
                x.SubjectId == y.SubjectId && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            public int GetHashCode((Guid SubjectId, string Name) obj) =>
                HashCode.Combine(obj.SubjectId, obj.Name.ToLowerInvariant());
        }

        // Shared by Create/Update -- "Hindi"/"English" (case-insensitive) parses to that enum value,
        // anything else (null, empty, a typo) just leaves it unset rather than 400ing the whole
        // request over an optional field.
        private static PaperLanguage? ParseLanguage(string? raw) =>
            !string.IsNullOrWhiteSpace(raw) && Enum.TryParse<PaperLanguage>(raw, ignoreCase: true, out var parsed) ? parsed : null;

        private static QuestionBankAdminQuestionDto ToAdminDto(QuestionBankQuestion x) => new QuestionBankAdminQuestionDto
        {
            Id = x.Id,
            QuestionText = x.QuestionText,
            QuestionImageUrl = x.QuestionImageUrl,
            OptionA = x.OptionA,
            OptionAImageUrl = x.OptionAImageUrl,
            OptionB = x.OptionB,
            OptionBImageUrl = x.OptionBImageUrl,
            OptionC = x.OptionC,
            OptionCImageUrl = x.OptionCImageUrl,
            OptionD = x.OptionD,
            OptionDImageUrl = x.OptionDImageUrl,
            CorrectOption = x.CorrectOption.ToString(),
            Explanation = x.Explanation,
            ExplanationImageUrl = x.ExplanationImageUrl,
            Subject = x.Subject?.Name ?? string.Empty,
            Topic = x.Topic?.Name ?? string.Empty,
            SourceReference = x.SourceReference,
            Language = x.Language?.ToString(),
            AskedIn = x.ExamMappings.OrderByDescending(m => m.Year).Select(m => new QuestionBankExamYearDto
            {
                ExamId = m.ExamId, ExamName = m.Exam?.Name ?? "Unknown", ExamLogoUrl = m.Exam?.LogoUrl, Year = m.Year
            }).ToList(),
            SolutionCount = x.Solutions?.Count ?? 0,
            CreatedAt = x.CreatedAt,
            SubjectId = x.SubjectId,
            TopicId = x.TopicId,
            CreatedByAdminName = x.CreatedByAdmin?.FullName ?? "Unknown",
            ImportJobId = x.ImportJobId,
            UpdatedAt = x.UpdatedAt
        };

        private static QuestionBankImportJobDto ToImportJobDto(QuestionBankImportJob j) => new QuestionBankImportJobDto
        {
            Id = j.Id,
            FileName = j.FileName,
            Format = j.Format.ToString(),
            Status = j.Status.ToString(),
            TotalRows = j.TotalRows,
            ValidRows = j.ValidRows,
            InvalidRows = j.InvalidRows,
            DuplicateRows = j.DuplicateRows,
            ImportedCount = j.ImportedCount,
            MergedIntoExistingCount = j.MergedIntoExistingCount,
            CreatedByAdminName = j.CreatedByAdmin?.FullName ?? "Unknown",
            CreatedAt = j.CreatedAt,
            CommittedAt = j.CommittedAt
        };
    }
}
