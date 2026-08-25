using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Serves files that used to live on local disk under wwwroot/uploads/{subfolder}/{file} via
    // app.UseStaticFiles() -- now backed by Azure Blob Storage's private "uploads" container (see
    // AzureBlobService). Deliberately kept at the same "/uploads/{subfolder}/{file}" route: every
    // QuestionImageUrl / PhotoUrl / chat- and dm-attachment value already in the database is of the
    // form "/uploads/question-images/{guid}.jpg", and FileStorageService.SaveFileAsync still returns
    // URLs in that exact shape -- this controller is just what now answers that URL instead of the
    // static-files middleware, so no data migration and no frontend change are needed.
    //
    // Left unauthenticated (matches the previous static-file behaviour): callers only ever receive a
    // URL for something they already had legitimate access to -- a question they can see, their own
    // profile, a chat/DM they're a participant of -- and blob names are unguessable GUIDs, not
    // sequential IDs.
    //
    // Deliberately NOT [ApiController]: that attribute makes NotFound()/BadRequest() auto-wrap into an
    // "application/problem+json" body. An <img> tag doing a cross-origin (frontend origin != API
    // origin) request that gets a JSON body back is exactly what Chrome's CORB (Cross-Origin Read
    // Blocking) is designed to block -- so a missing file would silently CORB-block in the browser
    // with no 404 ever visible in the console. Staying a plain ControllerBase keeps 404s as a bare,
    // bodyless 404, matching what app.UseStaticFiles() always returned for a missing file.
    [Route("uploads")]
    [AllowAnonymous]
    public class UploadedFilesController : ControllerBase
    {
        private static readonly Dictionary<string, string> ContentTypeByExtension = new()
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".webp"] = "image/webp",
            [".svg"] = "image/svg+xml",
            [".pdf"] = "application/pdf",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".ppt"] = "application/vnd.ms-powerpoint",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [".webm"] = "audio/webm",
            [".m4a"] = "audio/mp4",
            [".mp3"] = "audio/mpeg",
            [".ogg"] = "audio/ogg",
            [".wav"] = "audio/wav",
        };

        private readonly IAzureBlobService _blobService;

        public UploadedFilesController(IAzureBlobService blobService)
        {
            _blobService = blobService;
        }

        // GET /uploads/{subfolder}/{fileName} -- e.g. /uploads/question-images/550e8400-....jpg
        [HttpGet("{subfolder}/{fileName}")]
        public async Task<IActionResult> Get(string subfolder, string fileName)
        {
            // Defence-in-depth against path traversal, even though every real URL we hand out is one
            // we generated ourselves -- reject anything that isn't a plain "segment/segment" shape.
            if (subfolder.Contains('/') || subfolder.Contains("..") ||
                fileName.Contains('/') || fileName.Contains(".."))
            {
                return BadRequest();
            }

            var blobName = $"{subfolder}/{fileName}";

            Stream? stream;
            try
            {
                stream = await _blobService.DownloadAsync(blobName);
            }
            catch
            {
                return StatusCode(StatusCodes.Status502BadGateway);
            }

            if (stream == null) return NotFound();

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var contentType = ContentTypeByExtension.TryGetValue(ext, out var ct) ? ct : "application/octet-stream";
            return File(stream, contentType);
        }
    }
}
