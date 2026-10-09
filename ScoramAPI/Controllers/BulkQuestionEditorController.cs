using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Multi-paper + multi-question bulk editor (admin). All real work is in BulkQuestionEditService.
    // Permission: EditPaper for everything. ManageQuestionBank is NOT needed because bulk edits never modify
    // canonical Question Bank rows (those only receive stimulus links).
    [ApiController]
    [Route("api/admin/bulk-question-editor")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class BulkQuestionEditorController : ControllerBase
    {
        private readonly IBulkQuestionEditService _service;
        private readonly IAdminPermissionService _permissions;
        private readonly IFileStorageService _fileStorage;
        private readonly IAuditLogService _audit;
        private readonly ILogger<BulkQuestionEditorController> _logger;

        public BulkQuestionEditorController(IBulkQuestionEditService service, IAdminPermissionService permissions,
            IFileStorageService fileStorage, IAuditLogService audit, ILogger<BulkQuestionEditorController> logger)
        { _service = service; _permissions = permissions; _fileStorage = fileStorage; _audit = audit; _logger = logger; }

        private Task<bool> CanEditAsync() => _permissions.HasPermissionAsync(User, AdminPermission.EditPaper);

        // Step 1
        [HttpGet("papers")]
        public async Task<IActionResult> Papers([FromQuery] string? search, [FromQuery] Guid? examId, [FromQuery] int? year,
            [FromQuery] PaperLanguage? language, [FromQuery] PaperStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await CanEditAsync()) return Forbid();
            return Ok(await _service.ListPapersAsync(search, examId, year, language, status, page, pageSize, HttpContext.RequestAborted));
        }

        // Step 2  (paperIds repeated: ?paperIds=..&paperIds=..)
        [HttpGet("questions")]
        public async Task<IActionResult> Questions([FromQuery] List<Guid> paperIds, [FromQuery] Guid? paperId, [FromQuery] string? search,
            [FromQuery] string? subject, [FromQuery] string? topic, [FromQuery] string? stimulus, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
        {
            if (!await CanEditAsync()) return Forbid();
            try
            {
                var filter = new BulkQuestionFilterDto { PaperId = paperId, Search = search, Subject = subject, Topic = topic, Stimulus = stimulus };
                return Ok(await _service.ListQuestionsAsync(paperIds, filter, page, pageSize, HttpContext.RequestAborted));
            }
            catch (BulkEditException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpGet("filter-options")]
        public async Task<IActionResult> FilterOptions([FromQuery] List<Guid> paperIds)
        {
            if (!await CanEditAsync()) return Forbid();
            try { return Ok(await _service.GetFilterOptionsAsync(paperIds, HttpContext.RequestAborted)); }
            catch (BulkEditException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // Steps 5-6: nothing is written.
        [HttpPost("preview")]
        public async Task<IActionResult> Preview(BulkRequestDto request)
        {
            if (!await CanEditAsync()) return Forbid();
            try { return Ok(await _service.PreviewAsync(request, HttpContext.RequestAborted)); }
            catch (BulkEditException ex) { return BadRequest(new { message = ex.Message }); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk question editor preview failed");
                return StatusCode(500, new { message = "Couldn't prepare the preview. Nothing was changed." });
            }
        }

        // Step 7: validate-all-then-one-transaction. 200 with success=false (and no changes) on validation errors.
        [HttpPost("apply")]
        public async Task<IActionResult> Apply(BulkRequestDto request)
        {
            if (!await CanEditAsync()) return Forbid();
            var adminId = User.GetAdminId();
            try
            {
                _logger.LogInformation("Bulk question edit started by {AdminId}: {Type}", adminId, request?.Operation?.Type);
                var result = await _service.ApplyAsync(request!, adminId, HttpContext.RequestAborted);
                await _audit.LogAsync(adminId, result.Success ? "BulkQuestion.Applied" : "BulkQuestion.Rejected", "Paper", null,
                    $"op={request!.Operation.Type}; papers={request.PaperIds?.Count}; changed={result.QuestionsChanged}; ok={result.Success}");
                return Ok(result);
            }
            catch (BulkEditException ex) { return BadRequest(new { message = ex.Message }); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk question edit failed for admin {AdminId}", adminId);
                return StatusCode(500, new { message = "Something went wrong. No changes were applied." });
            }
        }

        // Upload once, then use the returned url in a SetImage operation: one stored file, many references.
        [HttpPost("upload-image")]
        [RequestSizeLimit(12 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage(IFormFile file)
        {
            if (!await CanEditAsync()) return Forbid();
            try
            {
                var url = await _fileStorage.SaveImageAsync(file, "question-images");
                if (url == null) return BadRequest(new { message = "No image was provided." });
                return Ok(new ImageUploadResultDto { Url = url });
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        }
    }
}
