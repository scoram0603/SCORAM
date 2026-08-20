using Azure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
    // AZURE BLOB STORAGE (Steps 10-13) -- generic file upload/view/download/delete for every use case
    // in Step 15 (question images, PYQ/exam PDFs, student profile images). Deliberately its own
    // controller rather than bolted onto QuestionsController/PapersController/UsersController -- those
    // already save their own images through the existing local-disk IFileStorageService (see
    // Services/FileStorageService.cs) and Step 21 says not to touch working, unrelated code. This is
    // new, additive infrastructure that existing/future modules can call into later; it doesn't
    // replace FileStorageService's current callers.
    //
    // Authorization (Step 17): open to any authenticated principal (Student, Admin or SuperAdmin) at
    // the class level, then narrowed per action --
    //   - QuestionImage / PyqPdf / ExamPdf: admin-managed content, so upload/delete require
    //     Admin/SuperAdmin. View/download are open to any authenticated user, same as the rest of the
    //     question-bank content these files are attached to.
    //   - ProfileImage: any authenticated user can upload their own; view/download/delete are
    //     restricted to the uploader themselves or an Admin/SuperAdmin, so a student can't pull
    //     another student's profile photo by guessing/incrementing a document id.
    [ApiController]
    [Route("api/documents")]
    [Authorize(Roles = "Student,Admin,SuperAdmin")]
    public class DocumentsController : ControllerBase
    {
        private static readonly Dictionary<DocumentCategory, string[]> AllowedExtensionsByCategory = new()
        {
            [DocumentCategory.QuestionImage] = new[] { ".jpg", ".jpeg", ".png", ".webp" },
            [DocumentCategory.ProfileImage] = new[] { ".jpg", ".jpeg", ".png", ".webp" },
            [DocumentCategory.PyqPdf] = new[] { ".pdf" },
            [DocumentCategory.ExamPdf] = new[] { ".pdf" },
        };

        // Secondary sanity check alongside the extension allowlist above (Step 6: "do not trust only
        // the client-provided MIME type" cuts both ways -- extension is the primary authority here,
        // this just catches an obviously mismatched Content-Type on top of it).
        private static readonly Dictionary<string, string[]> AllowedContentTypesByExtension = new()
        {
            [".jpg"] = new[] { "image/jpeg" },
            [".jpeg"] = new[] { "image/jpeg" },
            [".png"] = new[] { "image/png" },
            [".webp"] = new[] { "image/webp" },
            [".pdf"] = new[] { "application/pdf" },
        };

        private static readonly DocumentCategory[] AdminOnlyCategories =
        {
            DocumentCategory.QuestionImage, DocumentCategory.PyqPdf, DocumentCategory.ExamPdf
        };

        private readonly ScoramDbContext _db;
        private readonly IAzureBlobService _blobService;
        private readonly IConfiguration _config;
        private readonly ILogger<DocumentsController> _logger;

        public DocumentsController(ScoramDbContext db, IAzureBlobService blobService, IConfiguration config, ILogger<DocumentsController> logger)
        {
            _db = db;
            _blobService = blobService;
            _config = config;
            _logger = logger;
        }

        // POST /api/documents/upload -- multipart/form-data, fields: file, category
        // (category = "QuestionImage" | "PyqPdf" | "ExamPdf" | "ProfileImage")
        [HttpPost("upload")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(20 * 1024 * 1024)] // outer hard ceiling; the real, configurable limit is enforced in ValidateFile
        public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromForm] string category)
        {
            if (!Enum.TryParse<DocumentCategory>(category, ignoreCase: true, out var parsedCategory))
                return BadRequest(Fail($"category must be one of: {string.Join(", ", Enum.GetNames<DocumentCategory>())}."));

            var isStudent = User.IsInRole("Student");
            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

            if (AdminOnlyCategories.Contains(parsedCategory) && !isAdmin)
                return StatusCode(StatusCodes.Status403Forbidden, Fail("Only an admin can upload this type of document."));

            var validationError = ValidateFile(file, parsedCategory);
            if (validationError != null)
                return BadRequest(Fail(validationError));

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var blobName = _blobService.BuildBlobName(parsedCategory, ext);

            try
            {
                await using var stream = file.OpenReadStream();
                await _blobService.UploadAsync(blobName, stream, file.ContentType);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Azure Blob upload failed for category {Category}", parsedCategory);
                return StatusCode(StatusCodes.Status502BadGateway, Fail("Could not reach file storage right now. Please try again shortly."));
            }

            var document = new Document
            {
                FileName = file.FileName,
                BlobName = blobName,
                ContentType = file.ContentType,
                FileSize = file.Length,
                Category = parsedCategory,
                UploadedByUserId = isStudent ? User.GetUserId() : null,
                UploadedByAdminId = isAdmin ? User.GetAdminId() : null
            };

            try
            {
                _db.Documents.Add(document);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // The blob made it to storage but the metadata write failed -- clean up the orphan
                // rather than leaving a blob nothing in SQL ever points back to.
                _logger.LogError(ex, "Document metadata save failed after successful blob upload; cleaning up {BlobName}", blobName);
                try { await _blobService.DeleteAsync(blobName); } catch { /* best-effort cleanup */ }
                return StatusCode(StatusCodes.Status500InternalServerError, Fail("File upload failed. Please try again."));
            }

            _logger.LogInformation("Document {DocumentId} ({Category}) uploaded to {BlobName}", document.Id, document.Category, blobName);

            return Ok(new DocumentApiResponse<DocumentUploadResponseDto>
            {
                Success = true,
                Message = "File uploaded successfully.",
                Data = new DocumentUploadResponseDto
                {
                    Id = document.Id,
                    FileName = document.FileName,
                    ContentType = document.ContentType,
                    FileSize = document.FileSize,
                    BlobName = document.BlobName,
                    Category = document.Category.ToString(),
                    UploadedAt = document.UploadedAt
                }
            });
        }

        // GET /api/documents/{id}/view -- streams inline (no Content-Disposition filename), so the
        // browser renders JPG/PNG/WEBP directly and shows PDFs in its built-in viewer.
        [HttpGet("{id:guid}/view")]
        public Task<IActionResult> View(Guid id) => StreamDocument(id, inline: true);

        // GET /api/documents/{id}/download -- forces a download and preserves the original filename.
        [HttpGet("{id:guid}/download")]
        public Task<IActionResult> Download(Guid id) => StreamDocument(id, inline: false);

        private async Task<IActionResult> StreamDocument(Guid id, bool inline)
        {
            var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id);
            if (document == null) return NotFound(Fail("Document not found."));

            var accessDenied = CheckReadAccess(document);
            if (accessDenied != null) return accessDenied;

            Stream? blobStream;
            try
            {
                blobStream = await _blobService.DownloadAsync(document.BlobName);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Azure Blob download failed for {BlobName}", document.BlobName);
                return StatusCode(StatusCodes.Status502BadGateway, Fail("Could not reach file storage right now. Please try again shortly."));
            }

            if (blobStream == null)
            {
                _logger.LogWarning("Document {DocumentId} has no matching blob ({BlobName})", document.Id, document.BlobName);
                return NotFound(Fail("The file could not be found in storage."));
            }

            return inline
                ? File(blobStream, document.ContentType)
                : File(blobStream, document.ContentType, document.FileName);
        }

        // DELETE /api/documents/{id}
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id);
            if (document == null) return NotFound(Fail("Document not found."));

            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
            var isOwner = document.Category == DocumentCategory.ProfileImage
                          && User.IsInRole("Student")
                          && document.UploadedByUserId == User.GetUserId();

            if (AdminOnlyCategories.Contains(document.Category) && !isAdmin)
                return StatusCode(StatusCodes.Status403Forbidden, Fail("Only an admin can delete this type of document."));

            if (document.Category == DocumentCategory.ProfileImage && !isAdmin && !isOwner)
                return StatusCode(StatusCodes.Status403Forbidden, Fail("You don't have access to delete this file."));

            // Blob deletion first (Step 13) -- if it genuinely fails, keep the SQL row so the file
            // isn't "lost" from the app's point of view while still sitting in storage.
            try
            {
                await _blobService.DeleteAsync(document.BlobName);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Azure Blob deletion failed for {BlobName}; metadata retained", document.BlobName);
                return StatusCode(StatusCodes.Status502BadGateway, Fail("Could not delete the file from storage. Nothing was removed -- please try again."));
            }

            _db.Documents.Remove(document);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Document {DocumentId} deleted ({BlobName})", document.Id, document.BlobName);

            return Ok(new DocumentApiResponse<object?>
            {
                Success = true,
                Message = "File deleted successfully.",
                Data = null
            });
        }

        // Null = access granted. Non-null = the IActionResult the caller should return immediately.
        private IActionResult? CheckReadAccess(Document document)
        {
            var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
            if (isAdmin) return null;

            if (document.Category == DocumentCategory.ProfileImage)
            {
                if (document.UploadedByUserId == User.GetUserId()) return null;
                return StatusCode(StatusCodes.Status403Forbidden, Fail("You don't have access to this file."));
            }

            // QuestionImage / PyqPdf / ExamPdf -- part of the paid PYQ/question-bank content this
            // document is attached to; any authenticated student can view/download it, same as the
            // rest of that content today.
            return null;
        }

        private string? ValidateFile(IFormFile? file, DocumentCategory category)
        {
            if (file == null || file.Length == 0)
                return "The uploaded file is empty.";

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExtensions = AllowedExtensionsByCategory[category];
            if (!allowedExtensions.Contains(ext))
                return $"This document type only accepts: {string.Join(", ", allowedExtensions)}.";

            if (file.Length > MaxFileSizeBytes)
                return $"File must be {MaxFileSizeBytes / (1024 * 1024)} MB or smaller.";

            if (AllowedContentTypesByExtension.TryGetValue(ext, out var expectedTypes)
                && !expectedTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
                return "The file's content type doesn't match its extension.";

            return null;
        }

        // Reads "AzureBlobStorage:MaxFileSizeMB" from appsettings.json on every call (rather than
        // caching it at startup) so an ops change to the config takes effect without a restart --
        // consistent with how the rest of the config is read via IConfiguration elsewhere.
        private long MaxFileSizeBytes => (_config.GetValue<int?>("AzureBlobStorage:MaxFileSizeMB") ?? 10) * 1024L * 1024L;

        private static DocumentApiResponse<object?> Fail(string message) => new()
        {
            Success = false,
            Message = message,
            Data = null
        };
    }
}
