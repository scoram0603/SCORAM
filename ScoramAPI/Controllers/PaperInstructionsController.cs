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
    // Advanced Paper Instructions: ordered instruction blocks (text / math / image / table) shown before a
    // paper's questions. Independent of Question rows. Allowed on published papers too (never touches
    // questions). Images: upload via POST api/admin/shared-stimuli/upload-image and put the returned
    // url in an { type: "image" } block -- same stored file can be reused.
    [ApiController]
    [Route("api/admin/papers/{paperId:guid}/instructions")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class PaperInstructionsController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IAuditLogService _audit;

        public PaperInstructionsController(ScoramDbContext db, IAdminPermissionService permissions, IAuditLogService audit)
        { _db = db; _permissions = permissions; _audit = audit; }

        private async Task<bool> CanEditAsync() =>
            await _permissions.HasPermissionAsync(User, AdminPermission.EditPaper)
            || await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper);

        private static PaperInstructionDto ToDto(PaperInstruction i) => new()
        {
            Id = i.Id, PaperId = i.PaperId, Title = i.Title, Language = i.Language, DisplayOrder = i.DisplayOrder,
            Status = i.Status.ToString(), ContentBlocks = ContentBlocksJsonHelper.Parse(i.ContentBlocksJson),
            CreatedAt = i.CreatedAt, UpdatedAt = i.UpdatedAt
        };

        [HttpGet]
        public async Task<ActionResult<List<PaperInstructionDto>>> List(Guid paperId, [FromQuery] bool includeArchived = false)
        {
            if (!await CanEditAsync()) return Forbid();
            if (!await _db.Papers.AnyAsync(p => p.Id == paperId)) return NotFound(new { message = "Paper not found." });
            var q = _db.PaperInstructions.AsNoTracking().Where(i => i.PaperId == paperId);
            if (!includeArchived) q = q.Where(i => i.Status == PaperInstructionStatus.Active);
            var rows = await q.OrderBy(i => i.DisplayOrder).ThenBy(i => i.CreatedAt).ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        [HttpPost]
        public async Task<ActionResult<PaperInstructionDto>> Create(Guid paperId, PaperInstructionSaveDto dto)
        {
            if (!await CanEditAsync()) return Forbid();
            if (!await _db.Papers.AnyAsync(p => p.Id == paperId)) return NotFound(new { message = "Paper not found." });
            if (await _db.PaperInstructions.CountAsync(i => i.PaperId == paperId) >= PaperInstructionOrdering.MaxInstructionsPerPaper)
                return BadRequest(new { message = $"A paper can have at most {PaperInstructionOrdering.MaxInstructionsPerPaper} instructions." });

            string? json;
            try { json = StimulusContentValidator.ValidateAndSerialize(dto.ContentBlocks, requireContent: true); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }

            var maxOrder = await _db.PaperInstructions.Where(i => i.PaperId == paperId).MaxAsync(i => (int?)i.DisplayOrder);
            var entity = new PaperInstruction
            {
                PaperId = paperId, Title = dto.Title?.Trim(), Language = dto.Language?.Trim(), ContentBlocksJson = json,
                DisplayOrder = (maxOrder ?? -1) + 1, CreatedByAdminId = User.GetAdminId()
            };
            _db.PaperInstructions.Add(entity);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "PaperInstruction.Created", "Paper", paperId, entity.Title);
            return Ok(ToDto(entity));
        }

        [HttpPut("{id:guid}")]
        public async Task<ActionResult<PaperInstructionDto>> Update(Guid paperId, Guid id, PaperInstructionSaveDto dto)
        {
            if (!await CanEditAsync()) return Forbid();
            var e = await _db.PaperInstructions.FirstOrDefaultAsync(i => i.Id == id && i.PaperId == paperId);
            if (e == null) return NotFound(new { message = "Instruction not found." });

            string? json;
            try { json = StimulusContentValidator.ValidateAndSerialize(dto.ContentBlocks, requireContent: true); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }

            e.Title = dto.Title?.Trim(); e.Language = dto.Language?.Trim(); e.ContentBlocksJson = json;
            e.UpdatedByAdminId = User.GetAdminId(); e.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "PaperInstruction.Updated", "Paper", paperId, e.Title);
            return Ok(ToDto(e));
        }

        [HttpPost("{id:guid}/archive")]
        public Task<IActionResult> Archive(Guid paperId, Guid id) => SetStatusAsync(paperId, id, PaperInstructionStatus.Archived, "PaperInstruction.Archived");

        [HttpPost("{id:guid}/restore")]
        public Task<IActionResult> Restore(Guid paperId, Guid id) => SetStatusAsync(paperId, id, PaperInstructionStatus.Active, "PaperInstruction.Restored");

        private async Task<IActionResult> SetStatusAsync(Guid paperId, Guid id, PaperInstructionStatus status, string action)
        {
            if (!await CanEditAsync()) return Forbid();
            var e = await _db.PaperInstructions.FirstOrDefaultAsync(i => i.Id == id && i.PaperId == paperId);
            if (e == null) return NotFound(new { message = "Instruction not found." });
            if (e.Status != status)
            {
                e.Status = status;
                if (status == PaperInstructionStatus.Active) // restored -> goes to the end of the active list
                {
                    var max = await _db.PaperInstructions.Where(i => i.PaperId == paperId && i.Status == PaperInstructionStatus.Active).MaxAsync(i => (int?)i.DisplayOrder);
                    e.DisplayOrder = (max ?? -1) + 1;
                }
                e.UpdatedByAdminId = User.GetAdminId(); e.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                await _audit.LogAsync(User.GetAdminId(), action, "Paper", paperId, e.Title);
            }
            return Ok(new { status = e.Status.ToString() });
        }

        // PUT /api/admin/papers/{paperId}/instructions/reorder  { orderedIds: [...] }
        [HttpPut("reorder")]
        public async Task<IActionResult> Reorder(Guid paperId, PaperInstructionReorderDto dto)
        {
            if (!await CanEditAsync()) return Forbid();
            var active = await _db.PaperInstructions.Where(i => i.PaperId == paperId && i.Status == PaperInstructionStatus.Active).ToListAsync();
            try { PaperInstructionOrdering.Apply(active, dto.OrderedIds); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            await _db.SaveChangesAsync(); // one save: all-or-nothing
            await _audit.LogAsync(User.GetAdminId(), "PaperInstruction.Reordered", "Paper", paperId, $"{active.Count} instructions");
            return NoContent();
        }

        // Instructions are not referenced by anything, so a real delete is safe (spec: hard delete only
        // with zero dependencies). Archive is the reversible alternative.
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid paperId, Guid id)
        {
            if (!await CanEditAsync()) return Forbid();
            var e = await _db.PaperInstructions.FirstOrDefaultAsync(i => i.Id == id && i.PaperId == paperId);
            if (e == null) return NotFound(new { message = "Instruction not found." });
            _db.PaperInstructions.Remove(e);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "PaperInstruction.Deleted", "Paper", paperId, e.Title);
            return NoContent();
        }
    }
}
