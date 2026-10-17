using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // PUBLIC half of the app-config system: what the Flutter app (and the web client) read on launch to learn
    // about maintenance mode and forced / recommended updates. Anonymous on purpose -- the app must be able to
    // ask "am I too old?" before anyone has logged in. Returns only non-sensitive values.
    [ApiController]
    [Route("api/app-config")]
    [AllowAnonymous]
    public class AppConfigController : ControllerBase
    {
        private readonly IAppConfigService _config;

        public AppConfigController(IAppConfigService config) => _config = config;

        // GET /api/app-config
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var all = await _config.GetAllAsync(HttpContext.RequestAborted);
            string V(string key) => all.TryGetValue(key, out var v) ? v : string.Empty;
            string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

            return Ok(new
            {
                maintenance = new
                {
                    enabled = V(AppConfigService.MaintenanceEnabled) == "true",
                    message = V(AppConfigService.MaintenanceMessage)
                },
                features = new
                {
                    registration = V(AppConfigService.FeatureRegistration) == "true",
                    feedback = V(AppConfigService.FeatureFeedback) == "true"
                },
                android = new
                {
                    minVersion = NullIfEmpty(V("app.android.minVersion")),
                    recommendedVersion = NullIfEmpty(V("app.android.recommendedVersion")),
                    updateUrl = NullIfEmpty(V("app.android.updateUrl"))
                },
                ios = new
                {
                    minVersion = NullIfEmpty(V("app.ios.minVersion")),
                    recommendedVersion = NullIfEmpty(V("app.ios.recommendedVersion")),
                    updateUrl = NullIfEmpty(V("app.ios.updateUrl"))
                },
                releaseNotes = NullIfEmpty(V(AppConfigService.ReleaseNotes)),
                serverTime = DateTime.UtcNow
            });
        }

        // GET /api/app-config/status?platform=android&version=0.1.1
        // The server does the version comparison so every client (Flutter today, anything later) gets the
        // same answer: "ok" | "update_recommended" | "update_required".
        [HttpGet("status")]
        public async Task<IActionResult> Status([FromQuery] string platform, [FromQuery] string? version)
        {
            if (string.IsNullOrWhiteSpace(platform) || platform.Length > 20)
                return BadRequest(new { message = "platform is required (android or ios)." });

            var result = await _config.EvaluateVersionAsync(platform, version, HttpContext.RequestAborted);
            var all = await _config.GetAllAsync(HttpContext.RequestAborted);
            var maintenance = all.TryGetValue(AppConfigService.MaintenanceEnabled, out var m) && m == "true";

            return Ok(new
            {
                status = result.Status,
                minVersion = result.MinVersion,
                recommendedVersion = result.RecommendedVersion,
                updateUrl = result.UpdateUrl,
                releaseNotes = result.ReleaseNotes,
                maintenance = maintenance,
                maintenanceMessage = maintenance && all.TryGetValue(AppConfigService.MaintenanceMessage, out var msg) ? msg : null
            });
        }
    }

    public class UpdateAppConfigDto
    {
        public Dictionary<string, string>? Changes { get; set; }
        public string? Note { get; set; }
    }

    // ADMIN half: Admin > App Configuration. Every endpoint requires ManageAppConfig (SuperAdmin implicit);
    // keys flagged SuperAdminOnly (maintenance mode, registration, minimum versions) additionally require the
    // SuperAdmin role -- enforced inside AppConfigService.UpdateAsync, not in the UI.
    [ApiController]
    [Route("api/admin/app-config")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AdminAppConfigController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IAppConfigService _config;
        private readonly IAdminPermissionService _permissions;
        private readonly IAuditLogService _audit;

        public AdminAppConfigController(ScoramDbContext db, IAppConfigService config,
            IAdminPermissionService permissions, IAuditLogService audit)
        {
            _db = db;
            _config = config;
            _permissions = permissions;
            _audit = audit;
        }

        // GET /api/admin/app-config
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageAppConfig)) return Forbid();

            var values = await _config.GetAllAsync(HttpContext.RequestAborted);
            var stored = await _db.AppSettings.AsNoTracking().ToListAsync();
            var adminIds = stored.Where(s => s.UpdatedByAdminId != null).Select(s => s.UpdatedByAdminId!.Value).Distinct().ToList();
            var adminNames = await _db.Admins.Where(a => adminIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.FullName);
            var isSuperAdmin = User.IsInRole("SuperAdmin");

            return Ok(new
            {
                canEditCritical = isSuperAdmin,
                definitions = _config.Definitions.Select(d => new
                {
                    key = d.Key,
                    type = d.Type.ToString(),
                    group = d.Group,
                    label = d.Label,
                    description = d.Description,
                    superAdminOnly = d.SuperAdminOnly,
                    maxLength = d.MaxLength,
                    defaultValue = d.Default
                }),
                values,
                lastChanged = stored.ToDictionary(
                    s => s.Key,
                    s => new
                    {
                        updatedAt = s.UpdatedAt,
                        updatedBy = s.UpdatedByAdminId != null && adminNames.TryGetValue(s.UpdatedByAdminId.Value, out var n) ? n : null
                    })
            });
        }

        // PUT /api/admin/app-config   { "changes": { "<key>": "<value>" }, "note": "why" }
        [HttpPut]
        public async Task<IActionResult> Update(UpdateAppConfigDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageAppConfig)) return Forbid();
            if (dto.Changes == null || dto.Changes.Count == 0)
                return BadRequest(new { message = "No changes were supplied." });
            if (dto.Changes.Count > 25)
                return BadRequest(new { message = "Too many settings in one request." });

            var before = await _config.GetAllAsync(HttpContext.RequestAborted);
            var result = await _config.UpdateAsync(dto.Changes, User.GetAdminId(), User.IsInRole("SuperAdmin"), dto.Note, HttpContext.RequestAborted);

            switch (result.Status)
            {
                case AppConfigResultStatus.Forbidden:
                    return StatusCode(StatusCodes.Status403Forbidden, new { message = result.Error });
                case AppConfigResultStatus.Invalid:
                    return BadRequest(new { message = result.Error });
                case AppConfigResultStatus.NotFound:
                    return NotFound(new { message = result.Error });
            }

            if (result.ChangedKeys.Count > 0)
            {
                var after = await _config.GetAllAsync(HttpContext.RequestAborted);
                var detail = string.Join("; ", result.ChangedKeys.Select(k => $"{k}: '{Trunc(before.GetValueOrDefault(k))}' -> '{Trunc(after.GetValueOrDefault(k))}'"));
                await _audit.LogAsync(User.GetAdminId(), "AppConfig.Update", "AppConfig", null,
                    detail.Length > 950 ? detail.Substring(0, 950) : detail);
            }

            return Ok(new { changedKeys = result.ChangedKeys, batchId = result.BatchId });
        }

        // GET /api/admin/app-config/revisions?key=&page=1&pageSize=20
        [HttpGet("revisions")]
        public async Task<IActionResult> Revisions([FromQuery] string? key, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageAppConfig)) return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.AppSettingRevisions.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(key)) query = query.Where(r => r.Key == key);

            var total = await query.CountAsync();
            var rows = await query.OrderByDescending(r => r.ChangedAt)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var adminIds = rows.Where(r => r.ChangedByAdminId != null).Select(r => r.ChangedByAdminId!.Value).Distinct().ToList();
            var names = await _db.Admins.Where(a => adminIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.FullName);
            var labels = _config.Definitions.ToDictionary(d => d.Key, d => d.Label);

            return Ok(new
            {
                items = rows.Select(r => new
                {
                    id = r.Id,
                    key = r.Key,
                    label = labels.GetValueOrDefault(r.Key, r.Key),
                    oldValue = r.OldValue,
                    newValue = r.NewValue,
                    changedAt = r.ChangedAt,
                    changedBy = r.ChangedByAdminId != null ? names.GetValueOrDefault(r.ChangedByAdminId.Value) : null,
                    batchId = r.BatchId,
                    note = r.Note
                }),
                totalCount = total,
                page,
                pageSize
            });
        }

        // POST /api/admin/app-config/revisions/{id}/rollback -- restores the value this revision replaced.
        [HttpPost("revisions/{id:guid}/rollback")]
        public async Task<IActionResult> Rollback(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageAppConfig)) return Forbid();

            var result = await _config.RollbackAsync(id, User.GetAdminId(), User.IsInRole("SuperAdmin"), HttpContext.RequestAborted);
            switch (result.Status)
            {
                case AppConfigResultStatus.Forbidden:
                    return StatusCode(StatusCodes.Status403Forbidden, new { message = result.Error });
                case AppConfigResultStatus.Invalid:
                    return BadRequest(new { message = result.Error });
                case AppConfigResultStatus.NotFound:
                    return NotFound(new { message = result.Error });
            }

            await _audit.LogAsync(User.GetAdminId(), "AppConfig.Rollback", "AppConfig", id,
                string.Join(", ", result.ChangedKeys));
            return Ok(new { changedKeys = result.ChangedKeys });
        }

        // GET /api/admin/app-config/version-impact?platform=android&version=1.4.0
        // "How many known devices would be forced to update if the minimum became <version>?" Built from the
        // AppVersion each active device token last reported (DeviceToken.AppVersion) -- so it covers only
        // devices with push enabled, and says so. Prevents locking out an unexpectedly large share of users.
        [HttpGet("version-impact")]
        public async Task<IActionResult> VersionImpact([FromQuery] string platform, [FromQuery] string version)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageAppConfig)) return Forbid();

            var p = (platform ?? string.Empty).Trim().ToLowerInvariant();
            if (p != "android" && p != "ios") return BadRequest(new { message = "platform must be android or ios." });
            if (_config.CompareVersions(version, "0") == null) return BadRequest(new { message = "version must look like 1.2.3." });

            var wanted = p == "ios" ? "iOS" : "Android";
            var groups = await _db.DeviceTokens
                .Where(t => t.IsActive && t.Platform == wanted)
                .GroupBy(t => t.AppVersion)
                .Select(g => new { Version = g.Key, Count = g.Count() })
                .ToListAsync();

            int below = 0, atOrAbove = 0, unknown = 0;
            foreach (var g in groups)
            {
                var cmp = _config.CompareVersions(g.Version, version);
                if (cmp == null) unknown += g.Count;
                else if (cmp < 0) below += g.Count;
                else atOrAbove += g.Count;
            }

            return Ok(new
            {
                platform = p,
                version,
                devicesBelow = below,
                devicesAtOrAbove = atOrAbove,
                devicesUnknownVersion = unknown,
                basis = "Active devices that registered for push notifications and reported an app version."
            });
        }

        private static string Trunc(string? value)
        {
            if (value == null) return string.Empty;
            return value.Length > 60 ? value.Substring(0, 60) + "..." : value;
        }
    }
}
