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
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public Guid AdminId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
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
