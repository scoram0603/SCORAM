using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Bulk upload of Mock Tests and Practice Test templates -- see Services/TestBulkImportService.cs.
    //   POST  /api/admin/test-import/{kind}/preview      (multipart "file"; kind = mock | practice)
    //   POST  /api/admin/test-import/commit              { token, selectedIndexes? }
    //   GET   /api/admin/test-import/history?kind=
    //   GET   /api/admin/test-import/history/{jobId}
    //   POST  /api/admin/test-import/history/{jobId}/rollback
    //   GET   /api/admin/test-import/template/{kind}/{format}   (format = xlsx | csv | json)
    [ApiController]
    [Route("api/admin/test-import")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class TestBulkImportController : ControllerBase
    {
        private const long MaxFileBytes = 10 * 1024 * 1024;

        private readonly ITestBulkImportService _service;
        private readonly IAdminPermissionService _permissions;

        public TestBulkImportController(ITestBulkImportService service, IAdminPermissionService permissions)
        {
            _service = service;
            _permissions = permissions;
        }

        private static bool TryKind(string? raw, out TestImportKind kind)
        {
            switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "mock": case "mocktest": case "mocktests": kind = TestImportKind.MockTest; return true;
                case "practice": case "practicetemplate": case "practicetests": case "practice-tests": kind = TestImportKind.PracticeTemplate; return true;
                default: kind = default; return false;
            }
        }

        private static bool TryFormat(string fileName, out ImportFileFormat format)
        {
            switch (Path.GetExtension(fileName).ToLowerInvariant())
            {
                case ".csv": format = ImportFileFormat.Csv; return true;
                case ".xlsx": format = ImportFileFormat.Excel; return true;
                case ".json": format = ImportFileFormat.Json; return true;
                default: format = default; return false;
            }
        }

        [HttpPost("{kind}/preview")]
        [RequestSizeLimit(MaxFileBytes + 1024 * 1024)]
        public async Task<ActionResult<TestImportPreviewDto>> Preview(string kind, IFormFile file)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();
            if (!TryKind(kind, out var k)) return BadRequest(new { message = "Kind must be 'mock' or 'practice'." });
            if (file == null || file.Length == 0) return BadRequest(new { message = "Choose a file to upload." });
            if (file.Length > MaxFileBytes) return BadRequest(new { message = "The file is larger than 10 MB." });
            if (!TryFormat(file.FileName, out var format)) return BadRequest(new { message = "Upload a .xlsx, .csv or .json file." });

            await using var stream = file.OpenReadStream();
            // The parsers want a seekable stream (ClosedXML does); buffer it.
            await using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            buffer.Position = 0;

            var result = await _service.PreviewAsync(buffer, Path.GetFileName(file.FileName), format, k, User.GetAdminId());
            return Ok(result);
        }

        [HttpPost("commit")]
        public async Task<ActionResult<TestImportCommitResultDto>> Commit([FromBody] TestImportCommitDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();
            if (string.IsNullOrWhiteSpace(dto?.Token)) return BadRequest(new { message = "Missing preview token." });
            try
            {
                return Ok(await _service.CommitAsync(dto.Token, dto.SelectedIndexes, User.GetAdminId()));
            }
            catch (TestImportException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpGet("history")]
        public async Task<ActionResult<List<TestImportJobDto>>> History([FromQuery] string? kind)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();
            TestImportKind? k = null;
            if (!string.IsNullOrWhiteSpace(kind))
            {
                if (!TryKind(kind, out var parsed)) return BadRequest(new { message = "Kind must be 'mock' or 'practice'." });
                k = parsed;
            }
            return Ok(await _service.HistoryAsync(k));
        }

        [HttpGet("history/{jobId:guid}")]
        public async Task<ActionResult<TestImportJobDto>> GetJob(Guid jobId)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();
            var job = await _service.GetJobAsync(jobId);
            return job == null ? NotFound() : Ok(job);
        }

        [HttpPost("history/{jobId:guid}/rollback")]
        public async Task<ActionResult<TestImportRollbackResultDto>> Rollback(Guid jobId)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();
            try
            {
                return Ok(await _service.RollbackAsync(jobId, User.GetAdminId()));
            }
            catch (TestImportException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpGet("template/{kind}/{format}")]
        public async Task<IActionResult> Template(string kind, string format)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageTests)) return Forbid();
            if (!TryKind(kind, out var k)) return BadRequest(new { message = "Kind must be 'mock' or 'practice'." });

            var name = k == TestImportKind.MockTest ? "mock-test-template" : "practice-test-template";
            switch (format.Trim().ToLowerInvariant())
            {
                case "xlsx": case "excel":
                    return File(TestImportTemplates.Excel(k), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{name}.xlsx");
                case "csv":
                    return File(TestImportTemplates.Csv(k), "text/csv", $"{name}.csv");
                case "json":
                    return File(TestImportTemplates.Json(k), "application/json", $"{name}.json");
                default:
                    return BadRequest(new { message = "Format must be xlsx, csv or json." });
            }
        }
    }
}
