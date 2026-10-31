using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
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
    // Anonymous, but ONLY for the explicitly public asset folders in PrivateAttachmentRules.PublicFolders
    // (question/stimulus images, avatars, exam and organization logos, chat-room icons, landing media).
    // Private chat / direct-message attachments (chat-attachments, dm-attachments) and any folder that is
    // not on that list answer 404 here without touching storage -- those attachments are only delivered by
    // the authenticated, membership-checked endpoints GET /api/chat/messages/{id}/attachment and
    // GET /api/directmessages/messages/{id}/attachment. UploadsPrivacyMiddleware applies the same rule
    // earlier in the pipeline (before app.UseStaticFiles) so local wwwroot copies are covered as well.
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

        // Every file FileStorageService.SaveStreamAsync stores is named "{Guid}{ext}", and AzureBlobService.UploadAsync
        // refuses to overwrite an existing blob -- so the bytes behind a GUID-named URL never change. That is what
        // makes a one-year "immutable" cache lifetime safe for exactly those names. Anything else in a public folder
        // (a hand-placed file with a readable name, say) could legitimately be replaced in place, so it is served
        // exactly as before: no cache headers, no ETag.
        private static readonly Regex ImmutableFileNamePattern = new(
            @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\.[A-Za-z0-9]{1,8}\z",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private const string ImmutableCacheControl = "public, max-age=31536000, immutable";

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

            // Private attachment folders and unclassified folders are never served anonymously. Bare 404
            // (not 401/403) so the route does not confirm that a given attachment exists.
            if (!PrivateAttachmentRules.IsPublicFolder(subfolder))
            {
                return NotFound();
            }

            // PUBLIC + GUID-NAMED ONLY (private chat/DM attachments never reach this point -- they 404 above and
            // are served by authenticated endpoints with their own, non-public caching). The ETag is derived from
            // the name itself, which is valid precisely because the content behind a GUID name is immutable; that
            // lets a revalidation ("If-None-Match") be answered with 304 without a round trip to Azure Blob.
            var immutable = ImmutableFileNamePattern.IsMatch(fileName);
            EntityTagHeaderValue? entityTag = immutable ? new EntityTagHeaderValue($"\"{fileName}\"") : null;

            if (entityTag != null && MatchesIfNoneMatch(entityTag))
            {
                Response.Headers["Cache-Control"] = ImmutableCacheControl;
                Response.Headers["ETag"] = entityTag.ToString();
                return StatusCode(StatusCodes.Status304NotModified);
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

            if (ext == ".svg")
            {
                // Defense in depth: SaveStreamAsync's SanitizeSvg strips script-capable content before
                // an SVG is ever stored, but this costs nothing extra and covers anything uploaded
                // before that existed, or anything a sanitizer bug might miss. "sandbox" with no allow-
                // list blocks script execution, popups, and form submission even if this document is
                // opened directly (e.g. via a browser's "open image in new tab") rather than rendered
                // inside an <img> tag (where browsers already refuse to run scripts regardless).
                Response.Headers["Content-Security-Policy"] = "script-src 'none'; sandbox;";
            }

            // Only a file that was actually found gets cache headers -- 404/502 above stay uncached.
            if (entityTag != null)
            {
                Response.Headers["Cache-Control"] = ImmutableCacheControl;
                Response.Headers["ETag"] = entityTag.ToString();
            }

            return File(stream, contentType);
        }

        // True when the request's If-None-Match names our ETag (weak comparison, as RFC 9110 requires for
        // If-None-Match). A bare "*" is deliberately not treated as a match: that would claim the file exists
        // without checking storage.
        private bool MatchesIfNoneMatch(EntityTagHeaderValue entityTag)
        {
            foreach (var candidate in Request.GetTypedHeaders().IfNoneMatch)
            {
                if (candidate.Compare(entityTag, useStrongComparison: false)) return true;
            }
            return false;
        }
    }
}
