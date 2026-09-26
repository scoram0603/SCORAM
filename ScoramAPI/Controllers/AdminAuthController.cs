using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // SRS Section 3 (User Roles): Super Admin has full system access and creates/manages other
    // admins; Admin accounts are never self-registered. The first Super Admin on a brand-new database
    // is created by SuperAdminBootstrapService from the SUPERADMIN_EMAIL/SUPERADMIN_PASSWORD
    // environment variables -- see README for details -- not seeded in source anymore.
    [ApiController]
    [Route("api/admin")]
    public class AdminAuthController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly ITokenService _tokenService;
        private readonly IRefreshTokenService _refreshTokens;
        private readonly ISecurityStampService _securityStamps;
        private readonly IAuditLogService _audit;
        private readonly ITotpService _totp;
        private readonly IStagedDataCache _cache;

        // MFA challenge tokens (see the class-level MFA comment block further down) live here,
        // keyed by the opaque token itself -- 5 minutes is long enough for an admin to open their
        // authenticator app and type a code, short enough that a leaked/logged challenge token
        // (which on its own grants no access -- it's not a bearer credential, just proof the
        // password check already passed) isn't useful for long.
        private const string MfaChallengePrefix = "admin-mfa-challenge:";
        private static readonly TimeSpan MfaChallengeTtl = TimeSpan.FromMinutes(5);

        public AdminAuthController(
            ScoramDbContext db, ITokenService tokenService, IRefreshTokenService refreshTokens,
            ISecurityStampService securityStamps, IAuditLogService audit, ITotpService totp, IStagedDataCache cache)
        {
            _db = db;
            _tokenService = tokenService;
            _refreshTokens = refreshTokens;
            _securityStamps = securityStamps;
            _audit = audit;
            _totp = totp;
            _cache = cache;
        }

        [HttpPost("auth/login")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<AdminAuthResponseDto>> Login(AdminLoginDto dto)
        {
            var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Email == dto.Email);

            if (admin == null || !BCrypt.Net.BCrypt.Verify(dto.Password, admin.PasswordHash))
                return Unauthorized(new { message = "Invalid email or password." });

            if (!admin.IsActive)
                return Unauthorized(new { message = "This admin account has been deactivated." });

            if (admin.TotpEnabled)
            {
                // Password already verified above -- this challenge token proves that without being
                // a bearer credential itself (see its own field comment on AdminAuthResponseDto).
                var challengeToken = Guid.NewGuid().ToString("N");
                await _cache.SetAsync(MfaChallengePrefix + challengeToken, new MfaChallenge { AdminId = admin.Id }, MfaChallengeTtl);

                return Ok(new AdminAuthResponseDto
                {
                    MfaRequired = true,
                    MfaChallengeToken = challengeToken,
                    AdminId = admin.Id,
                    FullName = admin.FullName,
                    Email = admin.Email,
                    Role = admin.Role.ToString()
                });
            }

            var (token, expiresAt) = _tokenService.GenerateAdminToken(admin);
            var (refreshToken, _) = await _refreshTokens.IssueAsync(admin.Id, isAdmin: true, HttpContext.Connection.RemoteIpAddress?.ToString());
            await _audit.LogAsync(admin.Id, "Admin.Login", "Admin", admin.Id);
            return Ok(new AdminAuthResponseDto
            {
                Token = token,
                ExpiresAt = expiresAt,
                RefreshToken = refreshToken,
                AdminId = admin.Id,
                FullName = admin.FullName,
                Email = admin.Email,
                Role = admin.Role.ToString(),
                MustChangePassword = admin.MustChangePassword
            });
        }

        // POST /api/admin/auth/mfa/verify-login -- completes a login that Login returned with
        // MfaRequired=true. Accepts either a 6-digit TOTP code or one of the admin's backup codes.
        [HttpPost("auth/mfa/verify-login")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<AdminAuthResponseDto>> VerifyMfaLogin(AdminMfaVerifyLoginDto dto)
        {
            var challenge = await _cache.GetAsync<MfaChallenge>(MfaChallengePrefix + dto.MfaChallengeToken);
            if (challenge == null)
                return Unauthorized(new { message = "This login attempt has expired or is invalid. Please log in again." });

            var admin = await _db.Admins.FindAsync(challenge.AdminId);
            if (admin == null || !admin.IsActive || !admin.TotpEnabled || string.IsNullOrEmpty(admin.TotpSecret))
                return Unauthorized(new { message = "This account is no longer available for MFA login." });

            var validCode = _totp.ValidateCode(admin.TotpSecret, dto.Code) || TryConsumeBackupCode(admin, dto.Code);
            if (!validCode)
                return Unauthorized(new { message = "Invalid code." });

            // One-time use -- a second attempt with the same challenge token (even with a fresh,
            // otherwise-valid code) must go through Login again from scratch.
            await _cache.RemoveAsync(MfaChallengePrefix + dto.MfaChallengeToken);
            // Persists a backup-code consumption if TryConsumeBackupCode used one above; a no-op
            // otherwise (nothing on `admin` changed in the TOTP-code branch).
            await _db.SaveChangesAsync();

            var (token, expiresAt) = _tokenService.GenerateAdminToken(admin);
            var (refreshToken, _) = await _refreshTokens.IssueAsync(admin.Id, isAdmin: true, HttpContext.Connection.RemoteIpAddress?.ToString());
            await _audit.LogAsync(admin.Id, "Admin.Login.MfaVerified", "Admin", admin.Id);

            return Ok(new AdminAuthResponseDto
            {
                Token = token,
                ExpiresAt = expiresAt,
                RefreshToken = refreshToken,
                AdminId = admin.Id,
                FullName = admin.FullName,
                Email = admin.Email,
                Role = admin.Role.ToString(),
                MustChangePassword = admin.MustChangePassword
            });
        }

        // POST /api/admin/auth/mfa/setup -- (re)starts MFA setup for the CALLING admin's own account
        // (there's no "set up MFA for someone else" -- an admin who wants to require it for others
        // can only encourage them to do this themselves; see this feature's own top-level note on
        // why it's opt-in, not enforced). Calling this again before ever calling /mfa/enable simply
        // replaces the pending secret/backup codes with a fresh set -- harmless, since nothing was
        // active yet. Calling it again AFTER MFA is already enabled immediately invalidates the old
        // secret (TotpEnabled reset to false until re-confirmed) -- treat this as "start over", not
        // something to do accidentally.
        [Authorize(Roles = "Admin,SuperAdmin")]
        [HttpPost("auth/mfa/setup")]
        public async Task<ActionResult<AdminMfaSetupResponseDto>> SetupMfa()
        {
            var admin = await _db.Admins.FindAsync(User.GetAdminId());
            if (admin == null) return NotFound();

            var secret = _totp.GenerateSecret();
            var (backupCodes, hashes) = _totp.GenerateBackupCodes();

            admin.TotpSecret = secret;
            admin.TotpBackupCodeHashes = string.Join(",", hashes);
            admin.TotpEnabled = false; // not active until confirmed via /mfa/enable
            await _db.SaveChangesAsync();

            return Ok(new AdminMfaSetupResponseDto
            {
                Secret = secret,
                ProvisioningUri = _totp.GenerateProvisioningUri(secret, admin.Email, "Scoram"),
                BackupCodes = backupCodes
            });
        }

        // POST /api/admin/auth/mfa/enable -- confirms setup by proving the admin's authenticator
        // app actually works before MFA starts being required at login. Without this confirmation
        // step, a typo'd QR scan could lock the admin out on their very next login with no way back
        // in except a backup code they may not have saved yet either.
        [Authorize(Roles = "Admin,SuperAdmin")]
        [HttpPost("auth/mfa/enable")]
        public async Task<ActionResult> EnableMfa(AdminMfaEnableDto dto)
        {
            var admin = await _db.Admins.FindAsync(User.GetAdminId());
            if (admin == null) return NotFound();
            if (string.IsNullOrEmpty(admin.TotpSecret))
                return BadRequest(new { message = "Start MFA setup first (POST /api/admin/auth/mfa/setup)." });

            if (!_totp.ValidateCode(admin.TotpSecret, dto.Code))
                return BadRequest(new { message = "Invalid code. Check your authenticator app and try again." });

            admin.TotpEnabled = true;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(admin.Id, "Admin.Mfa.Enabled", "Admin", admin.Id);

            return Ok(new { message = "MFA enabled." });
        }

        // POST /api/admin/auth/mfa/disable -- requires the current password AND a valid code
        // (TOTP or backup), not just one or the other -- disabling MFA is a meaningful security
        // downgrade, so it deserves the same bar as changing the password itself, not a lower one.
        [Authorize(Roles = "Admin,SuperAdmin")]
        [HttpPost("auth/mfa/disable")]
        public async Task<ActionResult> DisableMfa(AdminMfaDisableDto dto)
        {
            var admin = await _db.Admins.FindAsync(User.GetAdminId());
            if (admin == null) return NotFound();

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, admin.PasswordHash))
                return BadRequest(new { message = "Current password is incorrect." });

            if (!admin.TotpEnabled || string.IsNullOrEmpty(admin.TotpSecret))
                return BadRequest(new { message = "MFA isn't enabled for this account." });

            var validCode = _totp.ValidateCode(admin.TotpSecret, dto.Code) || TryConsumeBackupCode(admin, dto.Code);
            if (!validCode)
                return BadRequest(new { message = "Invalid code." });

            admin.TotpEnabled = false;
            admin.TotpSecret = null;
            admin.TotpBackupCodeHashes = null;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(admin.Id, "Admin.Mfa.Disabled", "Admin", admin.Id);

            return Ok(new { message = "MFA disabled." });
        }

        // Removes `code`'s hash from admin.TotpBackupCodeHashes if present (one-time use -- a backup
        // code that's been used once is gone for good) and returns whether it matched. Mutates
        // `admin` in place but does NOT call SaveChangesAsync itself -- every caller above already
        // does its own save shortly after, so this avoids an extra redundant round-trip.
        private bool TryConsumeBackupCode(Admin admin, string code)
        {
            if (string.IsNullOrEmpty(admin.TotpBackupCodeHashes)) return false;

            var hashes = admin.TotpBackupCodeHashes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            var submittedHash = _totp.HashBackupCode(code);
            if (!hashes.Remove(submittedHash)) return false;

            admin.TotpBackupCodeHashes = string.Join(",", hashes);
            return true;
        }

        // PATCH /api/admin/auth/change-password -- any authenticated admin, for their own account.
        // Marked [AllowWhilePasswordChangeRequired] so this is reachable even when the account is
        // currently forced through the change-password gate (see MustChangePasswordFilter) -- it's
        // the one action that HAS to stay reachable in that state, or the gate would have no exit.
        [Authorize(Roles = "Admin,SuperAdmin")]
        [HttpPatch("auth/change-password")]
        [EnableRateLimiting("login")]
        [ScoramAPI.Middleware.AllowWhilePasswordChangeRequired]
        public async Task<ActionResult> ChangePassword(AdminChangePasswordDto dto)
        {
            var admin = await _db.Admins.FindAsync(User.GetAdminId());
            if (admin == null) return NotFound();

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, admin.PasswordHash))
                return BadRequest(new { message = "Current password is incorrect." });

            admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            admin.MustChangePassword = false;
            admin.SecurityStamp = Guid.NewGuid();
            await _db.SaveChangesAsync();
            await _securityStamps.InvalidateAsync(admin.Id, isAdmin: true);
            await _refreshTokens.RevokeAllAsync(admin.Id, isAdmin: true);
            await _audit.LogAsync(admin.Id, "Admin.ChangePassword", "Admin", admin.Id);

            // The token already in the caller's hands still carries the old mustChangePassword=true
            // AND stamp claims (JWTs are immutable once issued -- see TokenService's comment), so it
            // would keep getting blocked by both MustChangePasswordFilter and the stamp check in
            // Program.cs's OnTokenValidated until it naturally expires. Log in again to get a fresh one.
            return Ok(new { message = "Password updated successfully. Please log in again." });
        }

        // POST /api/admin/auth/refresh -- see AuthController.Refresh's comment for the full
        // rotation/reuse-detection reasoning; identical logic here, scoped to admin tokens (IsAdmin: true).
        [HttpPost("auth/refresh")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<AdminAuthResponseDto>> Refresh(RefreshTokenRequestDto dto)
        {
            var stored = await _refreshTokens.FindByRawTokenAsync(dto.RefreshToken, isAdmin: true);
            if (stored == null)
                return Unauthorized(new { message = "Invalid refresh token." });

            if (stored.RevokedAt != null)
            {
                // See AuthController.Refresh's matching comment -- same reasoning, admin session.
                await _refreshTokens.RevokeAllAsync(stored.PrincipalId, isAdmin: true);
                var compromisedAdmin = await _db.Admins.FindAsync(stored.PrincipalId);
                if (compromisedAdmin != null)
                {
                    compromisedAdmin.SecurityStamp = Guid.NewGuid();
                    await _db.SaveChangesAsync();
                    await _securityStamps.InvalidateAsync(compromisedAdmin.Id, isAdmin: true);
                }
                return Unauthorized(new { message = "This session is no longer valid. Please log in again." });
            }

            if (stored.ExpiresAt <= DateTime.UtcNow)
                return Unauthorized(new { message = "This session has expired. Please log in again." });

            var admin = await _db.Admins.FindAsync(stored.PrincipalId);
            if (admin == null || !admin.IsActive)
                return Unauthorized(new { message = "This account is no longer available." });

            var (newRawToken, newExpiresAt) = _tokenService.GenerateRefreshToken();
            var replacement = new RefreshToken
            {
                PrincipalId = admin.Id,
                IsAdmin = true,
                TokenHash = _tokenService.HashRefreshToken(newRawToken),
                ExpiresAt = newExpiresAt,
                CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString()
            };
            await _refreshTokens.RotateAsync(stored, replacement);

            var (accessToken, accessExpiresAt) = _tokenService.GenerateAdminToken(admin);
            return Ok(new AdminAuthResponseDto
            {
                Token = accessToken,
                ExpiresAt = accessExpiresAt,
                RefreshToken = newRawToken,
                AdminId = admin.Id,
                FullName = admin.FullName,
                Email = admin.Email,
                Role = admin.Role.ToString(),
                MustChangePassword = admin.MustChangePassword
            });
        }

        // POST /api/admin/auth/logout -- see AuthController.Logout's comment: ends every session for
        // this account, not just the one logging out.
        [Authorize(Roles = "Admin,SuperAdmin")]
        [HttpPost("auth/logout")]
        [ScoramAPI.Middleware.AllowWhilePasswordChangeRequired]
        public async Task<ActionResult> Logout()
        {
            var admin = await _db.Admins.FindAsync(User.GetAdminId());
            if (admin == null) return NotFound();

            admin.SecurityStamp = Guid.NewGuid();
            await _db.SaveChangesAsync();
            await _securityStamps.InvalidateAsync(admin.Id, isAdmin: true);
            await _refreshTokens.RevokeAllAsync(admin.Id, isAdmin: true);

            return Ok(new { message = "Logged out." });
        }

        // GET /api/admin/admins  (Super Admin only) -- "Monitor admin activities" / manage admins
        [HttpGet("admins")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<List<AdminResponseDto>>> ListAdmins()
        {
            var admins = await _db.Admins
                .OrderBy(a => a.FullName)
                .Select(a => new AdminResponseDto
                {
                    Id = a.Id,
                    FullName = a.FullName,
                    Email = a.Email,
                    Role = a.Role.ToString(),
                    IsActive = a.IsActive,
                    CreatedAt = a.CreatedAt,
                    Permissions = a.PermissionGrants.Select(g => g.Permission.ToString()).ToList()
                })
                .ToListAsync();

            return Ok(admins);
        }

        // POST /api/admin/admins  (Super Admin only) -- "Create and manage admins"
        [HttpPost("admins")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<AdminResponseDto>> CreateAdmin(AdminCreateDto dto)
        {
            if (await _db.Admins.AnyAsync(a => a.Email == dto.Email))
                return Conflict(new { message = "An admin account with this email already exists." });

            var admin = new Admin
            {
                FullName = dto.FullName,
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Role = dto.Role,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Admins.Add(admin);
            await _db.SaveChangesAsync();
            await _audit.LogAsync(User.GetAdminId(), "Admin.Create", "Admin", admin.Id, $"{admin.FullName} ({admin.Email}), role {admin.Role}");

            return Ok(new AdminResponseDto
            {
                Id = admin.Id,
                FullName = admin.FullName,
                Email = admin.Email,
                Role = admin.Role.ToString(),
                IsActive = admin.IsActive,
                CreatedAt = admin.CreatedAt
            });
        }

        // PATCH /api/admin/admins/{id}/status  (Super Admin only) -- activate/deactivate an admin,
        // e.g. to revoke access without deleting their history of uploaded questions/tests.
        [HttpPatch("admins/{id:guid}/status")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> SetStatus(Guid id, AdminStatusUpdateDto dto)
        {
            var admin = await _db.Admins.FindAsync(id);
            if (admin == null) return NotFound(new { message = "Admin not found." });

            if (admin.Id == User.GetAdminId())
                return BadRequest(new { message = "You can't change your own account's active status." });

            admin.IsActive = dto.IsActive;
            if (!dto.IsActive)
            {
                admin.SecurityStamp = Guid.NewGuid();
            }
            await _db.SaveChangesAsync();
            if (!dto.IsActive)
            {
                await _securityStamps.InvalidateAsync(admin.Id, isAdmin: true);
                await _refreshTokens.RevokeAllAsync(admin.Id, isAdmin: true);
            }
            await _audit.LogAsync(User.GetAdminId(), dto.IsActive ? "Admin.Activate" : "Admin.Deactivate", "Admin", admin.Id);

            return Ok(new { admin.Id, admin.IsActive });
        }

        // GET /api/admin/me/permissions -- any authenticated admin, for their own account. Used by the
        // frontend to decide what to show (e.g. hide the Review Queue nav item if you can't Publish) --
        // the actual enforcement always happens server-side regardless of what the UI shows.
        [HttpGet("me/permissions")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<AdminPermissionsResponseDto>> GetMyPermissions()
        {
            var adminId = User.GetAdminId();

            if (User.IsInRole("SuperAdmin"))
            {
                // Implicit -- a Super Admin has every permission regardless of grants (see
                // AdminPermissionService), so report the full set rather than whatever happens
                // to be in the grants table for them.
                return Ok(new AdminPermissionsResponseDto
                {
                    AdminId = adminId,
                    Permissions = Enum.GetValues<AdminPermission>().Select(p => p.ToString()).ToList()
                });
            }

            var permissions = await _db.AdminPermissionGrants
                .Where(g => g.AdminId == adminId)
                .Select(g => g.Permission.ToString())
                .ToListAsync();

            return Ok(new AdminPermissionsResponseDto { AdminId = adminId, Permissions = permissions });
        }

        // GET /api/admin/admins/{id}/permissions  (Super Admin only)
        [HttpGet("admins/{id:guid}/permissions")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<AdminPermissionsResponseDto>> GetPermissions(Guid id)
        {
            var adminExists = await _db.Admins.AnyAsync(a => a.Id == id);
            if (!adminExists) return NotFound(new { message = "Admin not found." });

            var permissions = await _db.AdminPermissionGrants
                .Where(g => g.AdminId == id)
                .Select(g => g.Permission.ToString())
                .ToListAsync();

            return Ok(new AdminPermissionsResponseDto { AdminId = id, Permissions = permissions });
        }

        // PUT /api/admin/admins/{id}/permissions  (Super Admin only) -- replace-all: send the complete
        // set of permissions this admin should have (see AdminPermissionsUpdateDto).
        [HttpPut("admins/{id:guid}/permissions")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<AdminPermissionsResponseDto>> SetPermissions(Guid id, AdminPermissionsUpdateDto dto)
        {
            var admin = await _db.Admins.FindAsync(id);
            if (admin == null) return NotFound(new { message = "Admin not found." });

            var existing = await _db.AdminPermissionGrants.Where(g => g.AdminId == id).ToListAsync();
            _db.AdminPermissionGrants.RemoveRange(existing);

            var distinctPermissions = dto.Permissions.Distinct();
            foreach (var permission in distinctPermissions)
            {
                _db.AdminPermissionGrants.Add(new AdminPermissionGrant { AdminId = id, Permission = permission });
            }

            await _db.SaveChangesAsync();
            var permissionList = distinctPermissions.Select(p => p.ToString()).ToList();
            await _audit.LogAsync(User.GetAdminId(), "Admin.SetPermissions", "Admin", id, string.Join(", ", permissionList));

            return Ok(new AdminPermissionsResponseDto
            {
                AdminId = id,
                Permissions = permissionList
            });
        }
    }
}
