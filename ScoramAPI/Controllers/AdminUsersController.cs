using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    public class UserStatusUpdateDto
    {
        public bool IsActive { get; set; }
        public string? Reason { get; set; }
    }

    // USER MANAGEMENT (student accounts) -- Admin > Users.
    //
    // Everything requires ManageUsers (SuperAdmin implicit), checked server-side on every action.
    //   * Lists mask email and phone; the detail view shows the email in full and the phone masked to its last
    //     four digits unless the caller is a SuperAdmin.
    //   * Never returns password hashes, security stamps, refresh-token hashes, OTP data or TOTP secrets.
    //   * "Verify account" is deliberately NOT an admin action: phone verification is proven by an MSG91 OTP
    //     (see AuthController), and letting an admin flip it would let a number be marked verified without
    //     ever being proven. The panel shows verification status; it does not forge it.
    //   * Suspension rotates the student's security stamp and revokes their refresh tokens, so existing app and
    //     web sessions stop working immediately (not at the next token expiry), exactly like the existing
    //     admin deactivation. Reason is mandatory, and every status change is audited with that reason --
    //     the audit trail IS the status history shown on the detail screen.
    [ApiController]
    [Route("api/admin/users")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AdminUsersController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IAuditLogService _audit;
        private readonly ISecurityStampService _securityStamps;
        private readonly IRefreshTokenService _refreshTokens;

        public AdminUsersController(ScoramDbContext db, IAdminPermissionService permissions, IAuditLogService audit,
            ISecurityStampService securityStamps, IRefreshTokenService refreshTokens)
        {
            _db = db;
            _permissions = permissions;
            _audit = audit;
            _securityStamps = securityStamps;
            _refreshTokens = refreshTokens;
        }

        private Task<bool> CanManageAsync() => _permissions.HasPermissionAsync(User, AdminPermission.ManageUsers);

        // GET /api/admin/users?search=&status=all|active|suspended&phoneVerified=&sort=newest|oldest|lastActive|name&page=&pageSize=
        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string? search, [FromQuery] string status = "all", [FromQuery] bool? phoneVerified = null,
            [FromQuery] string sort = "newest", [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
        {
            if (!await CanManageAsync()) return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.Users.AsNoTracking().AsQueryable();

            switch ((status ?? "all").Trim().ToLowerInvariant())
            {
                case "active": query = query.Where(u => u.IsActive); break;
                case "suspended": query = query.Where(u => !u.IsActive); break;
                case "all": break;
                default: return BadRequest(new { message = "status must be all, active or suspended." });
            }
            if (phoneVerified.HasValue) query = query.Where(u => u.PhoneVerified == phoneVerified.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                if (term.Length > 100) term = term.Substring(0, 100);
                var pattern = "%" + term.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
                query = query.Where(u =>
                    EF.Functions.Like(u.Username, pattern)
                    || EF.Functions.Like(u.FullName, pattern)
                    || EF.Functions.Like(u.Email, pattern)
                    || EF.Functions.Like(u.PhoneNumber, pattern));
            }

            query = (sort ?? "newest").ToLowerInvariant() switch
            {
                "oldest" => query.OrderBy(u => u.CreatedAt),
                "lastactive" => query.OrderByDescending(u => u.LastActiveAt),
                "name" => query.OrderBy(u => u.FullName),
                _ => query.OrderByDescending(u => u.CreatedAt)
            };

            var total = await query.CountAsync();
            var rows = await query.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(u => new
                {
                    u.Id, u.Username, u.FullName, u.Email, u.PhoneNumber, u.PhoneVerified, u.IsActive, u.CreatedAt, u.LastActiveAt
                })
                .ToListAsync();

            return Ok(new
            {
                items = rows.Select(u => new
                {
                    id = u.Id,
                    username = u.Username,
                    fullName = u.FullName,
                    email = MaskEmail(u.Email),
                    phone = MaskPhone(u.PhoneNumber),
                    phoneVerified = u.PhoneVerified,
                    isActive = u.IsActive,
                    createdAt = u.CreatedAt,
                    lastActiveAt = u.LastActiveAt
                }),
                totalCount = total,
                page,
                pageSize
            });
        }

        // GET /api/admin/users/{id}
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            if (!await CanManageAsync()) return Forbid();

            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound(new { message = "Student not found." });

            var now = DateTime.UtcNow;
            var attempts = await _db.StudentTestResults.CountAsync(a => a.UserId == id);
            var completed = await _db.StudentTestResults.CountAsync(a => a.UserId == id
                && (a.Status == TestAttemptStatus.Submitted || a.Status == TestAttemptStatus.AutoSubmitted));
            var feedbackCount = await _db.UserFeedbacks.CountAsync(f => f.UserId == id);
            var reportsFiled = await _db.QuestionReports.CountAsync(r => r.ReportedByUserId == id);
            var activeSessions = await _db.RefreshTokens.CountAsync(t => t.PrincipalId == id && !t.IsAdmin
                && t.RevokedAt == null && t.ExpiresAt > now);
            var activeDevices = await _db.DeviceTokens.CountAsync(t => t.UserId == id && t.IsActive);

            var exams = await _db.UserExamPreferences.Where(p => p.UserId == id)
                .Select(p => new { Name = p.Exam!.Name, p.IsPrimary }).ToListAsync();

            // Status history = audited User.* actions targeting this student, newest first.
            var history = await _db.AuditLogs
                .Where(a => a.TargetType == "User" && a.TargetId == id)
                .OrderByDescending(a => a.CreatedAt).Take(50)
                .Select(a => new
                {
                    a.Id, a.Action, a.Detail, a.CreatedAt,
                    Admin = a.Admin != null ? a.Admin.FullName : "Unknown"
                }).ToListAsync();

            var isSuper = User.IsInRole("SuperAdmin");
            return Ok(new
            {
                id = user.Id,
                username = user.Username,
                fullName = user.FullName,
                email = user.Email,
                phone = isSuper ? user.PhoneNumber : MaskPhone(user.PhoneNumber),
                phoneVerified = user.PhoneVerified,
                isActive = user.IsActive,
                createdAt = user.CreatedAt,
                lastActiveAt = user.LastActiveAt,
                stats = new { attempts, completedAttempts = completed, feedbackCount, questionReportsFiled = reportsFiled, activeSessions, activeDevices },
                exams = exams.Select(e => new { name = e.Name, isPrimary = e.IsPrimary }),
                statusHistory = history.Select(h => new { id = h.Id, action = h.Action, reason = h.Detail, at = h.CreatedAt, by = h.Admin })
            });
        }

        // PATCH /api/admin/users/{id}/status   { "isActive": false, "reason": "..." }
        [HttpPatch("{id:guid}/status")]
        public async Task<IActionResult> SetStatus(Guid id, UserStatusUpdateDto dto)
        {
            if (!await CanManageAsync()) return Forbid();

            var reason = (dto.Reason ?? string.Empty).Trim();
            if (reason.Length < 5)
                return BadRequest(new { message = "Please give a reason (at least 5 characters). It is saved in the audit trail." });
            if (reason.Length > 300) reason = reason.Substring(0, 300);

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound(new { message = "Student not found." });
            if (user.IsActive == dto.IsActive)
                return Ok(new { id = user.Id, isActive = user.IsActive, changed = false });

            user.IsActive = dto.IsActive;
            if (!dto.IsActive)
                user.SecurityStamp = Guid.NewGuid(); // invalidates every access token already issued
            await _db.SaveChangesAsync();

            if (!dto.IsActive)
            {
                await _securityStamps.InvalidateAsync(user.Id, isAdmin: false);
                await _refreshTokens.RevokeAllAsync(user.Id, isAdmin: false);
            }

            await _audit.LogAsync(User.GetAdminId(), dto.IsActive ? "User.Reactivate" : "User.Suspend", "User", user.Id,
                $"@{user.Username}: {reason}");

            return Ok(new { id = user.Id, isActive = user.IsActive, changed = true });
        }

        // GET /api/admin/users/{id}/sessions -- sign-ins that can still refresh, plus push-registered devices.
        [HttpGet("{id:guid}/sessions")]
        public async Task<IActionResult> Sessions(Guid id)
        {
            if (!await CanManageAsync()) return Forbid();
            if (!await _db.Users.AnyAsync(u => u.Id == id)) return NotFound(new { message = "Student not found." });

            var now = DateTime.UtcNow;
            var sessions = await _db.RefreshTokens.AsNoTracking()
                .Where(t => t.PrincipalId == id && !t.IsAdmin && t.RevokedAt == null && t.ExpiresAt > now)
                .OrderByDescending(t => t.CreatedAt).Take(50)
                .Select(t => new { t.Id, t.CreatedAt, t.ExpiresAt, t.CreatedByIp, t.UserAgent })
                .ToListAsync();

            var devices = await _db.DeviceTokens.AsNoTracking()
                .Where(t => t.UserId == id && t.IsActive)
                .OrderByDescending(t => t.LastUsedAt).Take(20)
                .Select(t => new { t.Id, t.Platform, t.AppVersion, t.LastUsedAt, t.CreatedAt })
                .ToListAsync();

            return Ok(new
            {
                sessions = sessions.Select(s => new
                {
                    id = s.Id,
                    signedInAt = s.CreatedAt,
                    expiresAt = s.ExpiresAt,
                    ip = s.CreatedByIp,
                    device = ClientInfo.Describe(s.UserAgent)
                }),
                devices = devices.Select(d => new
                {
                    id = d.Id, platform = d.Platform, appVersion = d.AppVersion, lastUsedAt = d.LastUsedAt, registeredAt = d.CreatedAt
                }),
                note = "A session is a sign-in that can still renew itself. Revoking one stops it renewing; its current access token (valid for up to its short lifetime) then expires on its own. Use 'Sign out everywhere' for immediate effect."
            });
        }

        // POST /api/admin/users/{id}/sessions/{sessionId}/revoke
        [HttpPost("{id:guid}/sessions/{sessionId:guid}/revoke")]
        public async Task<IActionResult> RevokeSession(Guid id, Guid sessionId)
        {
            if (!await CanManageAsync()) return Forbid();

            var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.Id == sessionId && t.PrincipalId == id && !t.IsAdmin);
            if (token == null) return NotFound(new { message = "Session not found." });
            if (token.RevokedAt == null) await _refreshTokens.RevokeAsync(token);

            await _audit.LogAsync(User.GetAdminId(), "User.RevokeSession", "User", id, "One session revoked.");
            return Ok(new { revoked = true });
        }

        // POST /api/admin/users/{id}/sessions/revoke-all -- immediate "sign out everywhere".
        [HttpPost("{id:guid}/sessions/revoke-all")]
        public async Task<IActionResult> RevokeAll(Guid id)
        {
            if (!await CanManageAsync()) return Forbid();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound(new { message = "Student not found." });

            user.SecurityStamp = Guid.NewGuid();
            await _db.SaveChangesAsync();
            await _securityStamps.InvalidateAsync(user.Id, isAdmin: false);
            await _refreshTokens.RevokeAllAsync(user.Id, isAdmin: false);

            await _audit.LogAsync(User.GetAdminId(), "User.RevokeAllSessions", "User", user.Id, $"@{user.Username}");
            return Ok(new { revoked = true });
        }

        private static string MaskEmail(string email)
        {
            var at = email.IndexOf('@');
            if (at <= 0) return "***";
            var local = email.Substring(0, at);
            var visible = local.Length <= 2 ? local.Substring(0, 1) : local.Substring(0, 2);
            return visible + "***" + email.Substring(at);
        }

        private static string MaskPhone(string phone)
        {
            var digits = phone.Where(char.IsDigit).ToArray();
            if (digits.Length <= 4) return "****";
            return new string('*', Math.Max(0, digits.Length - 4)) + new string(digits.Skip(digits.Length - 4).ToArray());
        }
    }
}
