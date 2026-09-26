using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.DTOs
{
    public class AdminLoginDto
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public class AdminAuthResponseDto
    {
        // Empty (never a real token) when MfaRequired is true -- see that field's own comment.
        // Frontend MUST check MfaRequired before treating this response as a completed login.
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        // See AuthResponseDto's matching comment in AuthDTOs.cs -- same mechanism, admin session.
        public string RefreshToken { get; set; } = string.Empty;
        public Guid AdminId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        // True right after SuperAdminBootstrapService creates the first SuperAdmin, or for a
        // pre-existing account the RemoveDefaultSuperAdminSeed migration flagged. The frontend should
        // route straight to the change-password screen and treat every other admin action as
        // unavailable until PATCH /api/admin/auth/change-password succeeds -- the server enforces
        // this too (see the "mustChangePassword" JWT claim and Program.cs's enforcement filter), so
        // this flag is only a UX hint, not the actual security boundary.
        public bool MustChangePassword { get; set; }

        // True when this admin has MFA enabled and the password check above succeeded, but no
        // access/refresh token has been issued yet -- Token/RefreshToken are both left empty in this
        // case. The frontend must prompt for a 6-digit code (or a backup code) and call
        // POST /api/admin/auth/mfa/verify-login with MfaChallengeToken to actually complete login.
        public bool MfaRequired { get; set; }
        // Opaque, single-use, short-lived (5 minutes) -- proves the password check already
        // succeeded, without granting any access itself. Null unless MfaRequired is true.
        public string? MfaChallengeToken { get; set; }
    }

    // POST /api/admin/auth/mfa/verify-login -- completes a login that came back with MfaRequired.
    public class AdminMfaVerifyLoginDto
    {
        [Required]
        public string MfaChallengeToken { get; set; } = string.Empty;
        [Required]
        public string Code { get; set; } = string.Empty;
    }

    // POST /api/admin/auth/mfa/setup -- starts (or restarts) MFA setup for the calling admin.
    public class AdminMfaSetupResponseDto
    {
        public string Secret { get; set; } = string.Empty;
        // For rendering a QR code client-side (e.g. via a JS QR library) -- also shown as plain text
        // for admins who'd rather type it into their authenticator app by hand.
        public string ProvisioningUri { get; set; } = string.Empty;
        // Shown to the admin exactly once, right now -- never retrievable again after this response.
        // Not yet active for login until confirmed via POST /api/admin/auth/mfa/enable.
        public List<string> BackupCodes { get; set; } = new();
    }

    // POST /api/admin/auth/mfa/enable -- confirms setup by proving the admin's authenticator is
    // actually working before MFA starts being required at login.
    public class AdminMfaEnableDto
    {
        [Required]
        public string Code { get; set; } = string.Empty;
    }

    // POST /api/admin/auth/mfa/disable -- requires both the current password and a valid code,
    // since disabling MFA is a meaningful security downgrade.
    public class AdminMfaDisableDto
    {
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;
        [Required]
        public string Code { get; set; } = string.Empty;
    }

    // PATCH /api/admin/auth/change-password -- any authenticated admin, for their own account.
    public class AdminChangePasswordDto
    {
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, MinLength(8)]
        public string NewPassword { get; set; } = string.Empty;
    }

    // Used by a Super Admin to create a new Admin (or another Super Admin) account.
    // There's no public self-registration endpoint for admins -- this is intentionally
    // only reachable by an authenticated Super Admin (see AdminAuthController).
    public class AdminCreateDto
    {
        [Required, MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(150)]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(8)]
        public string Password { get; set; } = string.Empty;

        public AdminRole Role { get; set; } = AdminRole.Admin;
    }

    public class AdminResponseDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        // Empty for a Super Admin, since they implicitly have every permission -- see
        // AdminPermissionService. Only meaningful for regular Admin accounts.
        public List<string> Permissions { get; set; } = new();
    }

    public class AdminStatusUpdateDto
    {
        [Required]
        public bool IsActive { get; set; }
    }
}
