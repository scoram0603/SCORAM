using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Admin Paper Preview. Read-only (viewing papers isn't permission-gated elsewhere either).
    // Returns exactly what a student would get: ACTIVE instructions + stimuli, questions in number order.
    [ApiController]
    [Route("api/admin/papers/{paperId:guid}/content-preview")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class PaperContentPreviewController : ControllerBase
    {
        private readonly IPaperContentService _content;
        public PaperContentPreviewController(IPaperContentService content) { _content = content; }

        [HttpGet]
        public async Task<IActionResult> Get(Guid paperId)
        {
            var dto = await _content.GetPreviewAsync(paperId, HttpContext.RequestAborted);
            return dto == null ? NotFound(new { message = "Paper not found." }) : Ok(dto);
        }
    }
}
