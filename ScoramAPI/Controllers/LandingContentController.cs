using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ScoramAPI.Data;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // LANDING CONTENT -- PUBLIC read. What the website's landing page asks for on load: the URL of every image an
    // admin has replaced, and the Smart Progress section copy (if an admin has edited it). Anonymous, rate limited,
    // cached for 60 s and invalidated when an admin saves. Returns ONLY whitelisted slots whose stored URL is one
    // this feature produced -- nothing private, nothing unpublished-draft-like, no admin identities.
    //
    // A slot with no entry means "show the website's bundled default image"; the site also falls back to that
    // default if a configured URL fails to load, so a bad reference can never blank the page.
    [ApiController]
    [Route("api/public/landing-content")]
    [AllowAnonymous]
    public class PublicLandingContentController : ControllerBase
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

        private readonly ScoramDbContext _db;
        private readonly IMemoryCache _cache;

        public PublicLandingContentController(ScoramDbContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        [HttpGet]
        [EnableRateLimiting("public-read")]
        public async Task<IActionResult> Get()
        {
            var cacheKey = $"public-landing-content:v{PublicContentCache.LandingVersion}";
            if (!_cache.TryGetValue(cacheKey, out object? payload) || payload == null)
            {
                var rows = await _db.LandingContentItems.AsNoTracking().ToListAsync();

                var media = new Dictionary<string, string>();
                foreach (var slot in LandingContentCatalog.Slots)
                {
                    var row = rows.FirstOrDefault(r => r.Key == LandingContentCatalog.MediaRowKey(slot.Key));
                    if (row != null && LandingContentCatalog.IsOwnMediaUrl(row.MediaUrl))
                        media[slot.Key] = row.MediaUrl!;
                }

                var smart = LandingContentCatalog.TryDeserialize(
                    rows.FirstOrDefault(r => r.Key == LandingContentCatalog.SmartProgressKey)?.TextJson);

                payload = new { media, smartProgress = smart };
                _cache.Set(cacheKey, payload, CacheTtl);
            }

            Response.Headers.CacheControl = "public, max-age=60";
            return Ok(payload);
        }
    }

    // LANDING CONTENT -- ADMIN write (Admin > Landing Page). Needs ManageLandingContent (SuperAdmin implicit);
    // every change is audited. Images go through the EXISTING storage pipeline (IFileStorageService -> the private
    // Azure Blob "uploads" container, served by UploadedFilesController) -- no new storage system, no base64 in
    // the database.
    //
    // Replacement order (so a failure can never leave a broken or empty reference):
    //   1. validate slot + size + extension, then FileStorageService validates content (magic bytes);
    //   2. the NEW blob is stored;
    //   3. only then is the reference swapped in the database;
    //   4. only after that commit is the OLD blob deleted (best effort).
    // If 2 or 3 fails the previous image/reference is untouched (and a just-stored new blob is cleaned up).
    [ApiController]
    [Route("api/admin/landing-content")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AdminLandingContentController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IFileStorageService _files;
        private readonly IAdminPermissionService _permissions;
        private readonly IAuditLogService _audit;
        private readonly ILogger<AdminLandingContentController> _logger;

        public AdminLandingContentController(ScoramDbContext db, IFileStorageService files,
            IAdminPermissionService permissions, IAuditLogService audit, ILogger<AdminLandingContentController> logger)
        {
            _db = db;
            _files = files;
            _permissions = permissions;
            _audit = audit;
            _logger = logger;
        }

        private Task<bool> CanAsync() => _permissions.HasPermissionAsync(User, AdminPermission.ManageLandingContent);

        // GET /api/admin/landing-content
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            if (!await CanAsync()) return Forbid();

            var rows = await _db.LandingContentItems.AsNoTracking().ToListAsync();
            var slots = LandingContentCatalog.Slots.Select(s =>
            {
                var row = rows.FirstOrDefault(r => r.Key == LandingContentCatalog.MediaRowKey(s.Key));
                var url = row != null && LandingContentCatalog.IsOwnMediaUrl(row.MediaUrl) ? row.MediaUrl : null;
                return new
                {
                    key = s.Key,
                    label = s.Label,
                    group = s.Group,
                    recommended = s.Recommended,
                    description = s.Description,
                    hasBundledDefault = s.HasBundledDefault,
                    customUrl = url,
                    updatedAt = url != null ? row!.UpdatedAt : (DateTime?)null
                };
            }).ToList();

            var smartRow = rows.FirstOrDefault(r => r.Key == LandingContentCatalog.SmartProgressKey);
            return Ok(new
            {
                slots,
                smartProgress = LandingContentCatalog.TryDeserialize(smartRow?.TextJson),
                smartProgressUpdatedAt = smartRow?.UpdatedAt,
                limits = new
                {
                    maxImageBytes = LandingContentCatalog.MaxImageBytes,
                    allowedExtensions = LandingContentCatalog.AllowedExtensions,
                    heading = LandingContentCatalog.MaxHeading,
                    description = LandingContentCatalog.MaxDescription,
                    highlights = LandingContentCatalog.MaxHighlights,
                    highlight = LandingContentCatalog.MaxHighlight,
                    ctaText = LandingContentCatalog.MaxCtaText
                }
            });
        }

        // POST /api/admin/landing-content/media/{slot}   (multipart/form-data, field "file")
        [HttpPost("media/{slot}")]
        [RequestSizeLimit(6 * 1024 * 1024)]
        public async Task<IActionResult> UploadMedia(string slot, IFormFile? file)
        {
            if (!await CanAsync()) return Forbid();
            if (!LandingContentCatalog.TryGetSlot(slot, out var def))
                return NotFound(new { message = "Unknown image slot." });

            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Choose an image file to upload." });
            if (file.Length > LandingContentCatalog.MaxImageBytes)
                return BadRequest(new { message = $"The image is too large. The maximum is {LandingContentCatalog.MaxImageBytes / (1024 * 1024)} MB." });
            if (!LandingContentCatalog.HasAllowedExtension(file.FileName))
                return BadRequest(new { message = "Only PNG, JPG or WebP images are allowed here." });

            // Step 2: store the new blob (FileStorageService re-checks size, extension and magic bytes).
            string? newUrl;
            try
            {
                newUrl = await _files.SaveImageAsync(file, LandingContentCatalog.MediaFolder);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Landing image upload failed for slot {Slot}", slot);
                return StatusCode(StatusCodes.Status502BadGateway,
                    new { message = "The upload failed, so the current image was kept. Please try again." });
            }

            if (!LandingContentCatalog.IsOwnMediaUrl(newUrl))
            {
                await _files.DeleteImageAsync(newUrl);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "The upload could not be stored, so the current image was kept." });
            }

            // Step 3: swap the reference.
            string? oldUrl;
            try
            {
                var key = LandingContentCatalog.MediaRowKey(def.Key);
                var row = await _db.LandingContentItems.FirstOrDefaultAsync(i => i.Key == key);
                oldUrl = row?.MediaUrl;
                if (row == null)
                {
                    row = new LandingContentItem { Key = key };
                    _db.LandingContentItems.Add(row);
                }
                row.MediaUrl = newUrl;
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedByAdminId = User.GetAdminId();
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Saving landing image reference failed for slot {Slot}", slot);
                await _files.DeleteImageAsync(newUrl); // don't orphan the new blob; old reference is unchanged
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "The image could not be saved, so the current image was kept." });
            }

            // Step 4: the new reference is committed -- now the previous custom upload can go (never a bundled default).
            if (LandingContentCatalog.IsOwnMediaUrl(oldUrl) && oldUrl != newUrl) await _files.DeleteImageAsync(oldUrl);

            PublicContentCache.BumpLanding();
            await _audit.LogAsync(User.GetAdminId(), "LandingContent.MediaReplaced", "LandingContent", null, def.Key);
            return Ok(new { key = def.Key, customUrl = newUrl });
        }

        // DELETE /api/admin/landing-content/media/{slot}  -> back to the website's bundled default
        [HttpDelete("media/{slot}")]
        public async Task<IActionResult> ResetMedia(string slot)
        {
            if (!await CanAsync()) return Forbid();
            if (!LandingContentCatalog.TryGetSlot(slot, out var def))
                return NotFound(new { message = "Unknown image slot." });

            var key = LandingContentCatalog.MediaRowKey(def.Key);
            var row = await _db.LandingContentItems.FirstOrDefaultAsync(i => i.Key == key);
            if (row == null) return NoContent();

            var oldUrl = row.MediaUrl;
            _db.LandingContentItems.Remove(row);
            await _db.SaveChangesAsync();

            if (LandingContentCatalog.IsOwnMediaUrl(oldUrl)) await _files.DeleteImageAsync(oldUrl);
            PublicContentCache.BumpLanding();
            await _audit.LogAsync(User.GetAdminId(), "LandingContent.MediaReset", "LandingContent", null, def.Key);
            return NoContent();
        }

        // PUT /api/admin/landing-content/smart-progress
        [HttpPut("smart-progress")]
        public async Task<IActionResult> SaveSmartProgress(SmartProgressContent dto)
        {
            if (!await CanAsync()) return Forbid();

            var (clean, error) = LandingContentCatalog.ValidateSmartProgress(dto);
            if (error != null) return BadRequest(new { message = error });

            var row = await _db.LandingContentItems.FirstOrDefaultAsync(i => i.Key == LandingContentCatalog.SmartProgressKey);
            if (row == null)
            {
                row = new LandingContentItem { Key = LandingContentCatalog.SmartProgressKey };
                _db.LandingContentItems.Add(row);
            }
            row.TextJson = LandingContentCatalog.Serialize(clean!);
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedByAdminId = User.GetAdminId();
            await _db.SaveChangesAsync();

            PublicContentCache.BumpLanding();
            await _audit.LogAsync(User.GetAdminId(), "LandingContent.SmartProgressSaved", "LandingContent", null,
                clean!.Published ? "published" : "hidden");
            return Ok(clean);
        }

        // DELETE /api/admin/landing-content/smart-progress -> back to the website's default Smart Progress copy
        [HttpDelete("smart-progress")]
        public async Task<IActionResult> ResetSmartProgress()
        {
            if (!await CanAsync()) return Forbid();

            var row = await _db.LandingContentItems.FirstOrDefaultAsync(i => i.Key == LandingContentCatalog.SmartProgressKey);
            if (row == null) return NoContent();

            _db.LandingContentItems.Remove(row);
            await _db.SaveChangesAsync();
            PublicContentCache.BumpLanding();
            await _audit.LogAsync(User.GetAdminId(), "LandingContent.SmartProgressReset", "LandingContent", null, null);
            return NoContent();
        }
    }
}
