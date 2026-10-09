using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Attach / detach / replace a Shared Stimulus on selected question occurrences of ONE paper.
    // Only relationship rows change -- questions, papers and stimuli are never edited or deleted.
    // Allowed on published papers too (additive repair of missing passages); it never alters question text.
    [ApiController]
    [Route("api/admin/papers/{paperId:guid}/stimulus-links")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class PaperStimulusLinksController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IStimulusLinkService _links;
        private readonly IAuditLogService _audit;
        private readonly ILogger<PaperStimulusLinksController> _logger;

        public PaperStimulusLinksController(ScoramDbContext db, IAdminPermissionService permissions, IStimulusLinkService links,
            IAuditLogService audit, ILogger<PaperStimulusLinksController> logger)
        { _db = db; _permissions = permissions; _links = links; _audit = audit; _logger = logger; }

        private async Task<bool> CanEditAsync() =>
            await _permissions.HasPermissionAsync(User, AdminPermission.EditPaper)
            || await _permissions.HasPermissionAsync(User, AdminPermission.UploadPaper);

        [HttpGet]
        public async Task<IActionResult> List(Guid paperId)
        {
            if (!await CanEditAsync()) return Forbid();
            if (!await _db.Papers.AnyAsync(p => p.Id == paperId)) return NotFound(new { message = "Paper not found." });
            return Ok(await _links.ListForPaperAsync(paperId));
        }

        [HttpPost("attach")]
        public Task<IActionResult> Attach(Guid paperId, StimulusAttachRequestDto dto) =>
            RunAsync(paperId, "SharedStimulus.Attached", dto.StimulusId.ToString(), dto.Targets.Count,
                () => _links.AttachAsync(paperId, dto.StimulusId, dto.Targets, User.GetAdminId()));

        [HttpPost("detach")]
        public Task<IActionResult> Detach(Guid paperId, StimulusDetachRequestDto dto) =>
            RunAsync(paperId, "SharedStimulus.Detached", dto.StimulusId.ToString(), dto.Targets.Count,
                () => _links.DetachAsync(paperId, dto.StimulusId, dto.Targets));

        [HttpPost("replace")]
        public Task<IActionResult> Replace(Guid paperId, StimulusReplaceRequestDto dto) =>
            RunAsync(paperId, "SharedStimulus.Replaced", $"{dto.FromStimulusId}->{dto.ToStimulusId}", dto.Targets.Count,
                () => _links.ReplaceAsync(paperId, dto.FromStimulusId, dto.ToStimulusId, dto.Targets));

        private async Task<IActionResult> RunAsync(Guid paperId, string auditAction, string detail, int requested, Func<Task<StimulusLinkOperationResultDto>> op)
        {
            if (!await CanEditAsync()) return Forbid();
            if (!await _db.Papers.AnyAsync(p => p.Id == paperId)) return NotFound(new { message = "Paper not found." });
            try
            {
                var result = await op();
                if (result.Changed > 0)
                    await _audit.LogAsync(User.GetAdminId(), auditAction, "Paper", paperId, $"{detail}; requested={requested}; changed={result.Changed}");
                return Ok(result);
            }
            catch (StimulusLinkException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stimulus link operation {Action} failed for paper {PaperId}", auditAction, paperId);
                return StatusCode(500, new { message = "Something went wrong. No changes were applied." });
            }
        }
    }
}
