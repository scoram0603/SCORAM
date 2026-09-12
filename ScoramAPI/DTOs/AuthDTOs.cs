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

        // MSG91 OTP -- the access-token the MSG91 widget's success(data) callback returned after
        // this PhoneNumber was actually OTP-verified client-side. AuthController.Register re-verifies
        // this server-side (see Msg91Service's own comment on why the client-side callback alone
        // can't be trusted) and cross-checks the number MSG91 confirms against PhoneNumber above --
        // registration fails if they don't match, rather than silently trusting whichever one the
        // form happened to submit.
        [Required]
        public string OtpAccessToken { get; set; } = string.Empty;

        // See CaptchaController/CaptchaService -- CaptchaId comes from GET /api/captcha/generate,
        // CaptchaAnswer is whatever the student typed for that challenge's question.
        [Required]
        public string CaptchaId { get; set; } = string.Empty;
        [Required]
        public int CaptchaAnswer { get; set; }

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

        // See RegisterDto's own comment -- same captcha challenge/answer shape. Login-with-OTP
        // (LoginOtpDto below) does NOT need this: a real SMS OTP round-trip is already a much
        // stronger (and MSG91-rate-limited) proof-of-not-a-bot than this captcha is.
        [Required]
        public string CaptchaId { get; set; } = string.Empty;
        [Required]
        public int CaptchaAnswer { get; set; }
    }

    // POST /api/auth/login-otp -- passwordless login for an EXISTING account, once its phone number
    // has been OTP-verified client-side via the MSG91 widget. Deliberately doesn't accept an email/
    // username alongside AccessToken -- see AuthController.LoginWithOtp's own comment on why the
    // verified phone number alone is what looks the account up, and why an unrecognized number
    // doesn't fall back to creating one.
    public class LoginOtpDto
    {
        [Required]
        public string AccessToken { get; set; } = string.Empty;
    }

    public class UsernameAvailabilityDto
    {
        public bool Available { get; set; }
        public string? Reason { get; set; } // set when Available is false and it's not simply "taken" (e.g. invalid format)
    }

    // GET /api/auth/otp-widget-config -- see AuthController.GetOtpWidgetConfig's own comment.
    public class OtpWidgetConfigDto
    {
        public string WidgetId { get; set; } = string.Empty;
        public string TokenAuth { get; set; } = string.Empty;
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
        public bool PhoneVerified { get; set; }
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
    // ChangePassword/ChangeEmail below require the current password as confirmation before making
    // the change -- same "prove you're still you" gate a bank/email provider uses for this kind of
    // sensitive edit. ChangePhone (further down) additionally requires OTP-verifying the NEW number
    // via MSG91 -- current password alone proves account ownership, not ownership of the new phone
    // being switched to. ChangeEmail has no equivalent OTP step (no email-OTP provider is wired up).

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

        // MSG91 OTP -- same idea as RegisterDto.OtpAccessToken: the access-token from the widget
        // having just OTP-verified NewPhoneNumber. AuthController.ChangePhone re-verifies it
        // server-side and cross-checks it matches NewPhoneNumber before accepting the change.
        [Required]
        public string OtpAccessToken { get; set; } = string.Empty;
    }

    public class ChangePhoneResponseDto
    {
        public string PhoneNumber { get; set; } = string.Empty;
    }

    // ---------- Profile: editable display info (Full Name / Username) ----------
    // Deliberately NOT password-gated like the Settings/Account & Security DTOs above --
    // FullName/Username aren't security-sensitive the way Email/Password/Phone are (they're just
    // display identity, already visible to anyone in chat/discussions), so there's no
    // "prove you're still you" step here, same as ChangePhoto having none either.
    public class UpdateProfileDto
    {
        [Required, MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        // Same rule as RegisterDto.Username -- kept in sync deliberately (see CheckUsername, which
        // both registration AND this endpoint's frontend form call).
        [Required, MinLength(3), MaxLength(30)]
        [RegularExpression(@"^[a-z0-9._]+$", ErrorMessage = "Username can only contain lowercase letters, numbers, dots, and underscores.")]
        public string Username { get; set; } = string.Empty;
    }

    public class UpdateProfileResponseDto
    {
        public string FullName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
    }

    // ---------- GET /api/auth/me ----------
    // A session created before some field existed on AuthResponseDto (this happened with
    // PhoneNumber -- it was never returned by Login/Register until now) has that field missing from
    // the person's cached session in the frontend, with no way to self-heal short of logging out and
    // back in. This endpoint lets the frontend silently refresh its cached user object once on app
    // load instead, so old sessions catch up without forcing a re-login -- and any FUTURE field added
    // to the user's profile gets this same self-healing for free, not just PhoneNumber today.
    public class MeResponseDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public bool PhoneVerified { get; set; }
        public string? PhotoUrl { get; set; }
        public bool NotifyOnGroupMessages { get; set; }
        public bool NotifyOnDirectMessages { get; set; }
    }
}
