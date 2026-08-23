using System.ComponentModel.DataAnnotations;

namespace ScoramAPI.DTOs
{
    public class RegisterDto
    {
        // Instagram-style handle: lowercase letters, numbers, underscore, dot. Enforced here as a
        // defense-in-depth backstop -- the frontend also live-checks availability via
        // GET /api/auth/check-username as the person types, so this validation firing at submit time
        // should be rare in practice.
        [Required, MinLength(3), MaxLength(30)]
        [RegularExpression(@"^[a-z0-9._]+$", ErrorMessage = "Username can only contain lowercase letters, numbers, dots, and underscores.")]
        public string Username { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(150)]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        // Optional: referral code of the user who invited this student
        public string? ReferralCode { get; set; }
    }

    public class LoginDto
    {
        // Either an email address or a username -- AuthController.Login figures out which.
        [Required]
        public string Identifier { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public class UsernameAvailabilityDto
    {
        public bool Available { get; set; }
        public string? Reason { get; set; } // set when Available is false and it's not simply "taken" (e.g. invalid format)
    }

    public class AuthResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        // Was never returned here before -- Profile/Settings need it to display + let the student
        // change their own number (see ChangePhoneDto below), same reason PhotoUrl is on this DTO.
        public string PhoneNumber { get; set; } = string.Empty;
        public string? PhotoUrl { get; set; }
        public bool NotifyOnGroupMessages { get; set; } = true;
        public bool NotifyOnDirectMessages { get; set; } = true;
    }

    public class NotificationPreferencesDto
    {
        public bool NotifyOnGroupMessages { get; set; }
        public bool NotifyOnDirectMessages { get; set; }
    }

    public class ProfilePhotoResponseDto
    {
        public string? PhotoUrl { get; set; }
    }

    // ---------- Settings: Account & Security ----------
    // All three require the current password as confirmation before making the change -- same
    // "prove you're still you" gate a bank/email provider uses for this kind of sensitive edit.
    // No OTP step yet (MSG91 integration is still pending -- see the OTP-registration discussion
    // earlier); once that's in place, ChangeEmail/ChangePhone can add an OTP-verify step here
    // without touching ChangePassword.

    public class ChangePasswordDto
    {
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, MinLength(6)]
        public string NewPassword { get; set; } = string.Empty;
    }

    public class ChangeEmailDto
    {
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(150)]
        public string NewEmail { get; set; } = string.Empty;
    }

    public class ChangeEmailResponseDto
    {
        public string Email { get; set; } = string.Empty;
    }

    public class ChangePhoneDto
    {
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string NewPhoneNumber { get; set; } = string.Empty;
    }

    public class ChangePhoneResponseDto
    {
        public string PhoneNumber { get; set; } = string.Empty;
    }
}
