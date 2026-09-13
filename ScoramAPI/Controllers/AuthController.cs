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
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly ITokenService _tokenService;
        private readonly IFileStorageService _fileStorage;
        private readonly IGamificationService _gamification;
        private readonly IMsg91Service _msg91;
        private readonly ICaptchaService _captcha;
        private readonly IConfiguration _config;

        public AuthController(ScoramDbContext db, ITokenService tokenService, IFileStorageService fileStorage, IGamificationService gamification, IMsg91Service msg91, ICaptchaService captcha, IConfiguration config)
        {
            _db = db;
            _tokenService = tokenService;
            _fileStorage = fileStorage;
            _gamification = gamification;
            _msg91 = msg91;
            _captcha = captcha;
            _config = config;
        }

        // GET /api/auth/otp-widget-config?platform=web|mobile -- public by design, same reasoning
        // as GET /api/push/vapid-public-key: WidgetId/TokenAuth are the OTP widget's own
        // client-facing credentials (see appsettings.json's own comment on why these -- unlike
        // Msg91:AuthKey -- are fine to hand out). MSG91 restricts a single widget to ONE
        // integration type (Web OR Mobile), so Scoram runs two separate widgets on the same MSG91
        // account -- this hands back whichever one's credentials match the caller's platform.
        // Today only the Flutter app actually calls this (ScoramWeb's own browser widget reads its
        // Web widget credentials from a build-time Vite env var instead -- see
        // ScoramWeb/src/lib/msg91.js -- so nothing there needs to change), but both platform
        // values are supported here so ScoramWeb could switch to fetching this too later without
        // another backend change.
        [HttpGet("otp-widget-config")]
        public ActionResult<OtpWidgetConfigDto> GetOtpWidgetConfig([FromQuery] string? platform)
        {
            string? widgetId;
            string? tokenAuth;

            switch (platform?.Trim().ToLowerInvariant())
            {
                case "web":
                    widgetId = _config["Msg91:WebWidgetId"];
                    tokenAuth = _config["Msg91:WebTokenAuth"];
                    break;
                case "mobile":
                    widgetId = _config["Msg91:MobileWidgetId"];
                    tokenAuth = _config["Msg91:MobileTokenAuth"];
                    break;
                default:
                    return BadRequest(new { message = "Query param 'platform' must be 'web' or 'mobile'." });
            }

            if (string.IsNullOrWhiteSpace(widgetId) || string.IsNullOrWhiteSpace(tokenAuth))
                return NotFound(new { message = $"Phone verification isn't configured on the server yet for platform '{platform}'." });

            return Ok(new OtpWidgetConfigDto { WidgetId = widgetId, TokenAuth = tokenAuth });
        }

        [HttpPost("register")]
        [EnableRateLimiting("register")]
        public async Task<ActionResult<AuthResponseDto>> Register(RegisterDto dto)
        {
            // Cheapest check first -- reject obvious bot/script submissions before spending an MSG91
            // API call verifying the phone OTP token below.
            if (!_captcha.Verify(dto.CaptchaId, dto.CaptchaAnswer))
                return BadRequest(new { message = "That captcha answer wasn't right. Please try again." });

            // MSG91 OTP -- verified server-side before anything else here, and the number IT confirms
            // (not dto.PhoneNumber as typed into the form) is what actually gets stored below. See
            // Msg91Service's own comment on why the widget's client-side success callback alone can't
            // be trusted, and RegisterDto.OtpAccessToken's comment on the cross-check.
            var verify = await _msg91.VerifyAccessTokenAsync(dto.OtpAccessToken);
            if (!verify.Success) return BadRequest(new { message = verify.ErrorMessage ?? "Phone verification failed." });

            var verifiedPhoneNumber = verify.PhoneNumber!;
            if (verifiedPhoneNumber != dto.PhoneNumber.Trim())
                return BadRequest(new { message = "The verified phone number doesn't match what you entered. Please try again." });

            var username = dto.Username.Trim().ToLowerInvariant();

            if (await _db.Users.AnyAsync(u => u.Username == username))
                return Conflict(new { message = "That username is already taken." });

            if (await _db.Users.AnyAsync(u => u.Email == dto.Email))
                return Conflict(new { message = "An account with this email already exists." });

            if (await _db.Users.AnyAsync(u => u.PhoneNumber == verifiedPhoneNumber))
                return Conflict(new { message = "An account with this phone number already exists." });

            var user = new User
            {
                Username = username,
                FullName = dto.FullName,
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                PhoneNumber = verifiedPhoneNumber,
                PhoneVerified = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            // Initialize gamification records for the new student
            // GAMIFICATION -- LastActiveDate uses IST (not UTC) calendar day, same as every streak
            // update in GamificationService, so day-one is consistent with day-two onward (see
            // GamificationService.ApplyStreak for why raw UTC dates would misattribute late-night
            // activity to the wrong day for Indian students).
            _db.UserStreaks.Add(new UserStreak
            {
                UserId = user.Id,
                CurrentStreak = 0,
                LongestStreak = 0,
                LastActiveDate = GamificationService.ToIstDate(DateTime.UtcNow)
            });
            _db.UserXPs.Add(new UserXP
            {
                UserId = user.Id,
                TotalXP = 0,
                CurrentLevel = UserLevel.Beginner
            });

            // GAMIFICATION -- if a valid referral code was supplied, reward whoever owns it. Looks up
            // the referrer by their permanent User.ReferralCode (generated lazily -- see
            // GamificationService.GetOrCreateReferralCodeAsync), not a one-time claimable row like
            // before: the same code can be reused by every friend a student invites.
            if (!string.IsNullOrWhiteSpace(dto.ReferralCode))
                await _gamification.ApplyReferralAsync(user.Id, dto.ReferralCode.Trim().ToUpperInvariant());

            await _db.SaveChangesAsync();

            var (token, expiresAt) = _tokenService.GenerateToken(user);
            user.LastActiveAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new AuthResponseDto
            {
                Token = token,
                ExpiresAt = expiresAt,
                UserId = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                PhoneVerified = user.PhoneVerified,
                PhotoUrl = user.PhotoUrl,
                NotifyOnGroupMessages = user.NotifyOnGroupMessages,
                NotifyOnDirectMessages = user.NotifyOnDirectMessages
            });
        }

        [HttpPost("login")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
        {
            if (!_captcha.Verify(dto.CaptchaId, dto.CaptchaAnswer))
                return BadRequest(new { message = "That captcha answer wasn't right. Please try again." });

            // Identifier is either an email or a username -- try both. Usernames are always stored
            // lowercase, so normalize before comparing; email lookups stay exact per how Register stores it.
            var identifier = dto.Identifier.Trim();
            var normalizedUsername = identifier.ToLowerInvariant();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == identifier || u.Username == normalizedUsername);

            if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
                return Unauthorized(new { message = "Invalid email/username or password." });

            if (!user.IsActive)
                return Unauthorized(new { message = "This account has been deactivated." });

            var (token, expiresAt) = _tokenService.GenerateToken(user);
            user.LastActiveAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new AuthResponseDto
            {
                Token = token,
                ExpiresAt = expiresAt,
                UserId = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                PhoneVerified = user.PhoneVerified,
                PhotoUrl = user.PhotoUrl,
                NotifyOnGroupMessages = user.NotifyOnGroupMessages,
                NotifyOnDirectMessages = user.NotifyOnDirectMessages
            });
        }

        // POST /api/auth/login-otp -- passwordless login for an EXISTING account: the person proves
        // they own a phone number via the MSG91 widget, and if that number matches a real account
        // (necessarily PhoneVerified -- see User.PhoneVerified's own comment on the only two ways it
        // ever becomes true), that's accepted as login. Deliberately does NOT create an account for
        // an unrecognized number -- registration still needs Username/FullName/Email/Password, none
        // of which OTP alone can supply, so this fails with a clear "no account" message instead of
        // silently doing something surprising.
        [HttpPost("login-otp")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<AuthResponseDto>> LoginWithOtp(LoginOtpDto dto)
        {
            var verify = await _msg91.VerifyAccessTokenAsync(dto.AccessToken);
            if (!verify.Success) return BadRequest(new { message = verify.ErrorMessage ?? "Phone verification failed." });

            var user = await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == verify.PhoneNumber);
            if (user == null)
                return Unauthorized(new { message = "No account found with this phone number. Please create an account first." });

            if (!user.IsActive)
                return Unauthorized(new { message = "This account has been deactivated." });

            var (token, expiresAt) = _tokenService.GenerateToken(user);
            user.LastActiveAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new AuthResponseDto
            {
                Token = token,
                ExpiresAt = expiresAt,
                UserId = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                PhoneVerified = user.PhoneVerified,
                PhotoUrl = user.PhotoUrl,
                NotifyOnGroupMessages = user.NotifyOnGroupMessages,
                NotifyOnDirectMessages = user.NotifyOnDirectMessages
            });
        }

        // GET /api/auth/me -- lets the frontend silently refresh its cached user object (see
        // MeResponseDto's comment in AuthDTOs.cs for why this exists).
        [Authorize(Roles = "Student")]
        [HttpGet("me")]
        public async Task<ActionResult<MeResponseDto>> Me()
        {
            var user = await _db.Users.FindAsync(User.GetUserId());
            if (user == null) return NotFound();

            return Ok(new MeResponseDto
            {
                UserId = user.Id,
                Username = user.Username,
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                PhoneVerified = user.PhoneVerified,
                PhotoUrl = user.PhotoUrl,
                NotifyOnGroupMessages = user.NotifyOnGroupMessages,
                NotifyOnDirectMessages = user.NotifyOnDirectMessages
            });
        }

        // GET /api/auth/check-username?username=xxx -- live availability check as the person types
        // during registration. Public, no auth needed.
        [HttpGet("check-username")]
        public async Task<ActionResult<UsernameAvailabilityDto>> CheckUsername([FromQuery] string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return Ok(new UsernameAvailabilityDto { Available = false, Reason = "Username can't be empty." });

            var normalized = username.Trim().ToLowerInvariant();

            if (normalized.Length < 3)
                return Ok(new UsernameAvailabilityDto { Available = false, Reason = "Username must be at least 3 characters." });

            if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, "^[a-z0-9._]+$"))
                return Ok(new UsernameAvailabilityDto { Available = false, Reason = "Only lowercase letters, numbers, dots, and underscores are allowed." });

            var taken = await _db.Users.AnyAsync(u => u.Username == normalized);
            return Ok(new UsernameAvailabilityDto { Available = !taken, Reason = taken ? "That username is already taken." : null });
        }

        // PATCH /api/auth/notification-preferences -- the two global "mute" switches (Group vs
        // Personal messages). Both default to true; this is the only way to turn either off.
        [Authorize(Roles = "Student")]
        [HttpPatch("notification-preferences")]
        public async Task<ActionResult<NotificationPreferencesDto>> UpdateNotificationPreferences(NotificationPreferencesDto dto)
        {
            var user = await _db.Users.FindAsync(User.GetUserId());
            if (user == null) return NotFound();

            user.NotifyOnGroupMessages = dto.NotifyOnGroupMessages;
            user.NotifyOnDirectMessages = dto.NotifyOnDirectMessages;
            await _db.SaveChangesAsync();

            return Ok(new NotificationPreferencesDto
            {
                NotifyOnGroupMessages = user.NotifyOnGroupMessages,
                NotifyOnDirectMessages = user.NotifyOnDirectMessages
            });
        }

        // POST /api/auth/profile-photo  (multipart/form-data, field name "file") -- lets a student
        // set their own avatar. Replaces any previous photo (old file is best-effort deleted so
        // uploads/avatars/ doesn't accumulate orphaned files as people change their photo over time).
        [Authorize(Roles = "Student")]
        [HttpPost("profile-photo")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<ActionResult<ProfilePhotoResponseDto>> UploadProfilePhoto(IFormFile file)
        {
            var user = await _db.Users.FindAsync(User.GetUserId());
            if (user == null) return NotFound();

            string? newUrl;
            try
            {
                newUrl = await _fileStorage.SaveImageAsync(file, "avatars");
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            if (newUrl == null) return BadRequest(new { message = "Choose an image to upload." });

            var oldUrl = user.PhotoUrl;
            user.PhotoUrl = newUrl;
            await _db.SaveChangesAsync();

            if (!string.IsNullOrEmpty(oldUrl)) await _fileStorage.DeleteImageAsync(oldUrl);

            return Ok(new ProfilePhotoResponseDto { PhotoUrl = user.PhotoUrl });
        }

        // DELETE /api/auth/profile-photo -- reverts to the initials avatar shown everywhere PhotoUrl
        // is null.
        [Authorize(Roles = "Student")]
        [HttpDelete("profile-photo")]
        public async Task<ActionResult<ProfilePhotoResponseDto>> RemoveProfilePhoto()
        {
            var user = await _db.Users.FindAsync(User.GetUserId());
            if (user == null) return NotFound();

            if (!string.IsNullOrEmpty(user.PhotoUrl))
            {
                await _fileStorage.DeleteImageAsync(user.PhotoUrl);
                user.PhotoUrl = null;
                await _db.SaveChangesAsync();
            }

            return Ok(new ProfilePhotoResponseDto { PhotoUrl = null });
        }

        // PATCH /api/auth/profile -- Full Name + Username only. No password gate (see the DTO's
        // comment in AuthDTOs.cs); Email/Phone/Password live under change-email/change-phone/
        // change-password below instead, precisely because those ARE security-sensitive.
        [Authorize(Roles = "Student")]
        [HttpPatch("profile")]
        public async Task<ActionResult<UpdateProfileResponseDto>> UpdateProfile(UpdateProfileDto dto)
        {
            var user = await _db.Users.FindAsync(User.GetUserId());
            if (user == null) return NotFound();

            var username = dto.Username.Trim().ToLowerInvariant();
            if (await _db.Users.AnyAsync(u => u.Id != user.Id && u.Username == username))
                return Conflict(new { message = "That username is already taken." });

            user.FullName = dto.FullName.Trim();
            user.Username = username;
            await _db.SaveChangesAsync();

            return Ok(new UpdateProfileResponseDto { FullName = user.FullName, Username = user.Username });
        }

        // ==========================================================================
        // Settings -- Account & Security
        // ==========================================================================

        // PATCH /api/auth/change-password -- reuses the "login" rate-limit policy since this also
        // verifies a password and is exactly the kind of endpoint credential-stuffing targets, same
        // reasoning as Login/Register above (see Program.cs's rate limiter comment block).
        [Authorize(Roles = "Student")]
        [HttpPatch("change-password")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult> ChangePassword(ChangePasswordDto dto)
        {
            var user = await _db.Users.FindAsync(User.GetUserId());
            if (user == null) return NotFound();

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                return BadRequest(new { message = "Current password is incorrect." });

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Password updated successfully." });
        }

        // PATCH /api/auth/change-email -- no OTP step yet (see the DTO's comment in AuthDTOs.cs),
        // so the current password is what stands in for "prove you're still you" for now.
        [Authorize(Roles = "Student")]
        [HttpPatch("change-email")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<ChangeEmailResponseDto>> ChangeEmail(ChangeEmailDto dto)
        {
            var user = await _db.Users.FindAsync(User.GetUserId());
            if (user == null) return NotFound();

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                return BadRequest(new { message = "Current password is incorrect." });

            var newEmail = dto.NewEmail.Trim();
            if (await _db.Users.AnyAsync(u => u.Id != user.Id && u.Email == newEmail))
                return Conflict(new { message = "An account with this email already exists." });

            user.Email = newEmail;
            await _db.SaveChangesAsync();

            return Ok(new ChangeEmailResponseDto { Email = user.Email });
        }

        // PATCH /api/auth/change-phone -- current password proves account ownership; the MSG91 OTP
        // on NewPhoneNumber proves ownership of the number being switched to. Both are required --
        // neither alone is sufficient (see ChangePhoneDto's own comment).
        [Authorize(Roles = "Student")]
        [HttpPatch("change-phone")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<ChangePhoneResponseDto>> ChangePhone(ChangePhoneDto dto)
        {
            var user = await _db.Users.FindAsync(User.GetUserId());
            if (user == null) return NotFound();

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                return BadRequest(new { message = "Current password is incorrect." });

            var verify = await _msg91.VerifyAccessTokenAsync(dto.OtpAccessToken);
            if (!verify.Success) return BadRequest(new { message = verify.ErrorMessage ?? "Phone verification failed." });

            var verifiedPhoneNumber = verify.PhoneNumber!;
            if (verifiedPhoneNumber != dto.NewPhoneNumber.Trim())
                return BadRequest(new { message = "The verified phone number doesn't match what you entered. Please try again." });

            if (await _db.Users.AnyAsync(u => u.Id != user.Id && u.PhoneNumber == verifiedPhoneNumber))
                return Conflict(new { message = "An account with this phone number already exists." });

            user.PhoneNumber = verifiedPhoneNumber;
            user.PhoneVerified = true;
            await _db.SaveChangesAsync();

            return Ok(new ChangePhoneResponseDto { PhoneNumber = user.PhoneNumber });
        }
    }
}
