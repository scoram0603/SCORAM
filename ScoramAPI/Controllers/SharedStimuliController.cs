using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Admin CRUD for Shared Stimulus (passage / table / chart / image common to several questions).
    // No hard delete exists on purpose: a stimulus that questions point at is archived, never removed.
    // Permission: reuses EditPaper / UploadPaper (same rule PapersController applies to paper edits).
    [ApiController]
    [Route("api/admin/shared-stimuli")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class SharedStimuliController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IFileStorageService _fileStorage;
        private readonly IAuditLogService _audit;
        private readonly ILogger<SharedStimuliController> _logger;

        public SharedStimuliController(ScoramDbContext db, IAdminPermissionService permissions, IFileStorageService fileStorage,
            IAuditLogService audit, ILogger<SharedStimuliController> logger)
        {
            _db = db; _permissions = permissions; _fileStorage = fileStorage; _audit = audit; _logger = logger;
        }

        private async Task<bool> CanEditAsync() =>
            await _permissions.HasPermissionAsync(User, AdminPermission.EditPaper)
            || await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper);

        // GET /api/admin/shared-stimuli?search=&status=&type=&language=&page=1&pageSize=20
        [HttpGet]
        public async Task<ActionResult<PagedResultDto<SharedStimulusListItemDto>>> List(
            [FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? type, [FromQuery] string? language,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await CanEditAsync()) return Forbid();
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var q = _db.SharedStimuli.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<SharedStimulusStatus>(status, true, out var st)) q = q.Where(s => s.Status == st);
            if (!string.IsNullOrWhiteSpace(type)) q = q.Where(s => s.Type == type);
            if (!string.IsNullOrWhiteSpace(language)) q = q.Where(s => s.Language == language);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                q = q.Where(s => (s.BusinessId != null && s.BusinessId.Contains(term)) || s.Title.Contains(term) || (s.Description != null && s.Description.Contains(term)));
            }

            var total = await q.CountAsync();
            var items = await q.OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(s => new SharedStimulusListItemDto
                {
                    Id = s.Id, BusinessId = s.BusinessId, Title = s.Title, Type = s.Type, Language = s.Language,
                    Status = s.Status.ToString(), LinkedQuestionCount = s.Links.Count,
                    CreatedByName = s.CreatedByAdmin != null ? s.CreatedByAdmin.FullName : null,
                    CreatedAt = s.CreatedAt, UpdatedAt = s.UpdatedAt
                }).ToListAsync();

            return new PagedResultDto<SharedStimulusListItemDto> { Items = items, Total = total, Page = page, PageSize = pageSize };
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<SharedStimulusDetailDto>> Get(Guid id)
        {
            if (!await CanEditAsync()) return Forbid();
            var s = await _db.SharedStimuli.AsNoTracking().Include(x => x.CreatedByAdmin).FirstOrDefaultAsync(x => x.Id == id);
            if (s == null) return NotFound(new { message = "Shared Stimulus not found." });
            var count = await _db.PaperQuestionStimuli.CountAsync(l => l.SharedStimulusId == id);
            return ToDetail(s, count);
        }

        [HttpPost]
        public async Task<ActionResult<SharedStimulusDetailDto>> Create(SharedStimulusSaveDto dto)
        {
            if (!await CanEditAsync()) return Forbid();
            if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest(new { message = "Title is required." });

            string? json;
            try { json = StimulusContentValidator.ValidateAndSerialize(dto.ContentBlocks, requireContent: true); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }

            var entity = new SharedStimulus
            {
                Title = dto.Title.Trim(), Description = dto.Description?.Trim(), Type = dto.Type?.Trim(), Language = dto.Language?.Trim(),
                ContentBlocksJson = json, CreatedByAdminId = User.GetAdminId()
            };
            _db.SharedStimuli.Add(entity);
            await _db.SaveChangesAsync(); // BusinessId (STM0001...) is assigned here by the central generator

            await _audit.LogAsync(User.GetAdminId(), "SharedStimulus.Created", "SharedStimulus", entity.Id, $"{entity.BusinessId} \"{entity.Title}\"");
            _logger.LogInformation("Shared stimulus {BusinessId} created by {AdminId}", entity.BusinessId, User.GetAdminId());
            return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDetail(entity, 0));
        }

        [HttpPut("{id:guid}")]
        public async Task<ActionResult<SharedStimulusDetailDto>> Update(Guid id, SharedStimulusSaveDto dto)
        {
            if (!await CanEditAsync()) return Forbid();
            if (string.IsNullOrWhiteSpace(dto.Title)) return BadRequest(new { message = "Title is required." });
            var s = await _db.SharedStimuli.FirstOrDefaultAsync(x => x.Id == id);
            if (s == null) return NotFound(new { message = "Shared Stimulus not found." });

            string? json;
            try { json = StimulusContentValidator.ValidateAndSerialize(dto.ContentBlocks, requireContent: true); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }

            s.Title = dto.Title.Trim(); s.Description = dto.Description?.Trim(); s.Type = dto.Type?.Trim(); s.Language = dto.Language?.Trim();
            s.ContentBlocksJson = json;
            s.UpdatedByAdminId = User.GetAdminId(); s.UpdatedAt = DateTime.UtcNow;
            // NOTE: images removed from the content are NOT deleted from storage -- another record
            // (or an older version someone is still viewing) may reference the same file.
            await _db.SaveChangesAsync();

            await _audit.LogAsync(User.GetAdminId(), "SharedStimulus.Updated", "SharedStimulus", s.Id, $"{s.BusinessId} \"{s.Title}\"");
            var count = await _db.PaperQuestionStimuli.CountAsync(l => l.SharedStimulusId == id);
            return ToDetail(s, count);
        }

        [HttpPost("{id:guid}/archive")]
        public Task<IActionResult> Archive(Guid id) => SetStatusAsync(id, SharedStimulusStatus.Archived, "SharedStimulus.Archived");

        [HttpPost("{id:guid}/restore")]
        public Task<IActionResult> Restore(Guid id) => SetStatusAsync(id, SharedStimulusStatus.Active, "SharedStimulus.Restored");

        private async Task<IActionResult> SetStatusAsync(Guid id, SharedStimulusStatus status, string action)
        {
            if (!await CanEditAsync()) return Forbid();
            var s = await _db.SharedStimuli.FirstOrDefaultAsync(x => x.Id == id);
            if (s == null) return NotFound(new { message = "Shared Stimulus not found." });
            if (s.Status != status) // idempotent
            {
                s.Status = status; s.UpdatedByAdminId = User.GetAdminId(); s.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                await _audit.LogAsync(User.GetAdminId(), action, "SharedStimulus", s.Id, s.BusinessId);
            }
            var count = await _db.PaperQuestionStimuli.CountAsync(l => l.SharedStimulusId == id);
            return Ok(new { status = s.Status.ToString(), linkedQuestionCount = count });
        }

        // GET /api/admin/shared-stimuli/{id}/questions -- which question occurrences use it (capped preview).
        [HttpGet("{id:guid}/questions")]
        public async Task<ActionResult<List<StimulusLinkedQuestionDto>>> LinkedQuestions(Guid id)
        {
            if (!await CanEditAsync()) return Forbid();
            if (!await _db.SharedStimuli.AnyAsync(x => x.Id == id)) return NotFound(new { message = "Shared Stimulus not found." });

            var a = await _db.PaperQuestionStimuli.AsNoTracking().Where(x => x.SharedStimulusId == id && x.Question != null)
                .Select(x => new StimulusLinkedQuestionDto
                {
                    PaperId = x.Question!.PaperId ?? Guid.Empty,
                    PaperName = x.Question.Paper != null ? x.Question.Paper.Exam!.Name + " " + x.Question.Paper.Year : "",
                    QuestionNumber = x.Question.QuestionNumber, Source = "Paper", QuestionId = x.QuestionId,
                    QuestionTextPreview = x.Question.QuestionText.Length > 140 ? x.Question.QuestionText.Substring(0, 140) : x.Question.QuestionText
                }).Take(500).ToListAsync();

            var b = await _db.PaperQuestionStimuli.AsNoTracking().Where(x => x.SharedStimulusId == id && x.PaperQuestionBankLink != null)
                .Select(x => new StimulusLinkedQuestionDto
                {
                    PaperId = x.PaperQuestionBankLink!.PaperId,
                    PaperName = x.PaperQuestionBankLink.Paper!.Exam!.Name + " " + x.PaperQuestionBankLink.Paper.Year,
                    QuestionNumber = x.PaperQuestionBankLink.QuestionNumber, Source = "QuestionBank", LinkId = x.PaperQuestionBankLinkId,
                    QuestionTextPreview = x.PaperQuestionBankLink.QuestionBankQuestion!.QuestionText.Length > 140
                        ? x.PaperQuestionBankLink.QuestionBankQuestion.QuestionText.Substring(0, 140)
                        : x.PaperQuestionBankLink.QuestionBankQuestion.QuestionText
                }).Take(500).ToListAsync();

            return a.Concat(b).OrderBy(x => x.PaperName).ThenBy(x => x.QuestionNumber).ToList();
        }

        // POST /api/admin/shared-stimuli/upload-image  (multipart "file") -> { url }
        // The admin UI then puts { type: "image", content: url } into the stimulus blocks. The same stored
        // file can be referenced by any number of stimuli/questions -- attach never re-uploads.
        [HttpPost("upload-image")]
        [RequestSizeLimit(12 * 1024 * 1024)]
        public async Task<ActionResult<ImageUploadResultDto>> UploadImage(IFormFile file)
        {
            if (!await CanEditAsync()) return Forbid();
            try
            {
                var url = await _fileStorage.SaveImageAsync(file, "stimulus-images");
                if (url == null) return BadRequest(new { message = "No image was provided." });
                return new ImageUploadResultDto { Url = url };
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        }

        private static SharedStimulusDetailDto ToDetail(SharedStimulus s, int linkedCount) => new()
        {
            Id = s.Id, BusinessId = s.BusinessId, Title = s.Title, Description = s.Description, Type = s.Type, Language = s.Language,
            Status = s.Status.ToString(), LinkedQuestionCount = linkedCount, CreatedByName = s.CreatedByAdmin?.FullName,
            CreatedAt = s.CreatedAt, UpdatedAt = s.UpdatedAt, ContentBlocks = ContentBlocksJsonHelper.Parse(s.ContentBlocksJson)
        };
    }
}
