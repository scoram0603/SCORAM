using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Extensions;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // SESSION & DEVICE MANAGEMENT for ADMIN accounts (student sessions live under Admin > Users). SuperAdmin
    // only: seeing where other admins are signed in, and ending those sessions, is account-security
    // administration. Built on the existing RefreshToken table (one row per sign-in that can still renew) -- no
    // sessions are invented; User-Agent is recorded from login onward, so older sign-ins show "Unknown device".
    [ApiController]
    [Route("api/admin/sessions")]
    [Authorize(Roles = "SuperAdmin")]
    public class AdminSessionsController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IAuditLogService _audit;
        private readonly ISecurityStampService _securityStamps;
        private readonly IRefreshTokenService _refreshTokens;

        public AdminSessionsController(ScoramDbContext db, IAuditLogService audit,
            ISecurityStampService securityStamps, IRefreshTokenService refreshTokens)
        {
            _db = db;
            _audit = audit;
            _securityStamps = securityStamps;
            _refreshTokens = refreshTokens;
        }

        // GET /api/admin/sessions
        [HttpGet]
        public async Task<IActionResult> List()
        {
            var now = DateTime.UtcNow;
            var me = User.GetAdminId();

            var rows = await _db.RefreshTokens.AsNoTracking()
                .Where(t => t.IsAdmin && t.RevokedAt == null && t.ExpiresAt > now)
                .OrderByDescending(t => t.CreatedAt).Take(200)
                .Select(t => new { t.Id, t.PrincipalId, t.CreatedAt, t.ExpiresAt, t.CreatedByIp, t.UserAgent })
                .ToListAsync();

            var adminIds = rows.Select(r => r.PrincipalId).Distinct().ToList();
            var admins = await _db.Admins.AsNoTracking().Where(a => adminIds.Contains(a.Id))
                .Select(a => new { a.Id, a.FullName, a.Email, Role = a.Role.ToString(), a.BusinessId })
                .ToDictionaryAsync(a => a.Id);

            return Ok(rows.Select(r => new
            {
                id = r.Id,
                adminId = r.PrincipalId,
                adminName = admins.TryGetValue(r.PrincipalId, out var a) ? a.FullName : "Unknown",
                adminBusinessId = a?.BusinessId,
                role = a?.Role,
                signedInAt = r.CreatedAt,
                expiresAt = r.ExpiresAt,
                ip = r.CreatedByIp,
                device = ClientInfo.Describe(r.UserAgent),
                isYou = r.PrincipalId == me
            }));
        }

        // POST /api/admin/sessions/{id}/revoke
        [HttpPost("{id:guid}/revoke")]
        public async Task<IActionResult> Revoke(Guid id)
        {
            var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.Id == id && t.IsAdmin);
            if (token == null) return NotFound(new { message = "Session not found." });
            if (token.RevokedAt == null) await _refreshTokens.RevokeAsync(token);

            await _audit.LogAsync(User.GetAdminId(), "AdminSession.Revoke", "Admin", token.PrincipalId, "One session revoked.");
            return Ok(new { revoked = true });
        }

        // POST /api/admin/sessions/admins/{adminId}/force-logout -- immediate: rotates the admin's security
        // stamp (every access token stops working) and revokes all their refresh tokens.
        [HttpPost("admins/{adminId:guid}/force-logout")]
        public async Task<IActionResult> ForceLogout(Guid adminId)
        {
            if (adminId == User.GetAdminId())
                return BadRequest(new { message = "Use Log Out to end your own sessions." });

            var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Id == adminId);
            if (admin == null) return NotFound(new { message = "Admin not found." });

            admin.SecurityStamp = Guid.NewGuid();
            await _db.SaveChangesAsync();
            await _securityStamps.InvalidateAsync(admin.Id, isAdmin: true);
            await _refreshTokens.RevokeAllAsync(admin.Id, isAdmin: true);

            await _audit.LogAsync(User.GetAdminId(), "AdminSession.ForceLogout", "Admin", admin.Id, admin.FullName);
            return Ok(new { revoked = true });
        }
    }
}
