using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Enums;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // SYSTEM HEALTH -- Admin > Administration > System Health. Read-only, no secrets: reports reachability and
    // a few cheap operational counters. Anything the platform cannot measure from inside the API (CPU, disk,
    // external uptime monitors, push-provider delivery) is reported as unavailable rather than guessed.
    [ApiController]
    [Route("api/admin/system-health")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AdminSystemHealthController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IInstantSearchService _search;
        private readonly IAdminPermissionService _permissions;
        private readonly IWebHostEnvironment _env;

        public AdminSystemHealthController(ScoramDbContext db, IInstantSearchService search,
            IAdminPermissionService permissions, IWebHostEnvironment env)
        {
            _db = db;
            _search = search;
            _permissions = permissions;
            _env = env;
        }

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ViewAnalytics)) return Forbid();

            var checkedAt = DateTime.UtcNow;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool dbOk;
            try { dbOk = await _db.Database.CanConnectAsync(ct); } catch { dbOk = false; }
            sw.Stop();

            bool searchOk;
            try { searchOk = await _search.IsHealthyAsync(); } catch { searchOk = false; }

            return Ok(new
            {
                checkedAt,
                environment = _env.EnvironmentName,
                services = new object[]
                {
                    new { key = "api", label = "API", status = "healthy", detail = "Responding" },
                    new { key = "database", label = "Database", status = dbOk ? "healthy" : "down",
                          detail = dbOk ? $"Reachable ({sw.ElapsedMilliseconds} ms)" : "Cannot connect" },
                    new { key = "search", label = "Search index", status = searchOk ? "healthy" : "degraded",
                          detail = searchOk ? "Healthy" : "Unavailable -- the database fallback search is serving requests" }
                },
                unavailable = new[]
                {
                    new { key = "failedLogins", label = "Failed login attempts", reason = "Failed sign-ins are not written to the audit log, so there is nothing to count." },
                    new { key = "pushDelivery", label = "Push delivery rate", reason = "Delivery receipts from the push provider are not recorded." },
                    new { key = "serverResources", label = "CPU / memory / disk", reason = "Host metrics are not exposed to the API; use your hosting provider's monitoring." },
                    new { key = "uptime", label = "Historical uptime", reason = "Needs an external uptime monitor pointed at /health." }
                }
            });
        }
    }
}
