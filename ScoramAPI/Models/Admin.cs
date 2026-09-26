using System.ComponentModel.DataAnnotations;
using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    public class Admin
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required, MaxLength(150)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public AdminRole Role { get; set; } = AdminRole.Admin;

        public bool IsActive { get; set; } = true;

        // Forces a password change before the account can do anything else once authenticated (see
        // the "mustChangePassword" JWT claim in TokenService.GenerateAdminToken and the enforcement
        // filter in Program.cs). Set true for the auto-bootstrapped first SuperAdmin (see
        // SuperAdminBootstrapService) and, via migration, for any pre-existing seeded SuperAdmin row
        // still on its original default password -- never for an admin whose password has already
        // been changed.
        public bool MustChangePassword { get; set; } = false;

        // Regenerated whenever an active session should be forced to re-authenticate (password
        // change, logout, deactivation, or a role/permission change -- see AdminAuthController). Baked
        // into every admin access token as the "stamp" claim at login time and checked against this
        // column on every authenticated request (see SecurityStampService and Program.cs's
        // OnTokenValidated).
        public Guid SecurityStamp { get; set; } = Guid.NewGuid();

        // ---------- MFA (TOTP) ----------
        // Opt-in per admin, not enforced account-wide -- forcing it on every existing admin at once
        // would lock people out on their next login with no warning. TotpSecret is null until the
        // admin starts setup (see AdminAuthController's mfa/setup endpoint) and TotpEnabled stays
        // false until they've proven they can generate a valid code with it (mfa/enable) -- a secret
        // alone, generated but never confirmed working, must never gate login.
        public string? TotpSecret { get; set; }
        public bool TotpEnabled { get; set; } = false;

        // Hashed backup codes (see TotpService.HashBackupCode), comma-joined -- consumed one at a
        // time (removed from this list) if the admin's authenticator device is ever unavailable.
        // Never stores the plaintext codes themselves; those are shown to the admin exactly once,
        // at generation time, and never persisted.
        public string? TotpBackupCodeHashes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<Question> QuestionsUploaded { get; set; } = new List<Question>();
        public ICollection<MockTest> MockTestsCreated { get; set; } = new List<MockTest>();
        public ICollection<CurrentAffair> CurrentAffairsPosted { get; set; } = new List<CurrentAffair>();
        public ICollection<JobAlert> JobAlertsPosted { get; set; } = new List<JobAlert>();
        public ICollection<AdminTask> AssignedTasks { get; set; } = new List<AdminTask>();
        public ICollection<AdminPermissionGrant> PermissionGrants { get; set; } = new List<AdminPermissionGrant>();
        public ICollection<Paper> UploadedPapers { get; set; } = new List<Paper>();
    }
}
