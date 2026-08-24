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

        public AuthController(ScoramDbContext db, ITokenService tokenService, IFileStorageService fileStorage, IGamificationService gamification)
        {
            _db = db;
            _tokenService = tokenService;
            _fileStorage = fileStorage;
            _gamification = gamification;
        }

        [HttpPost("register")]
        [EnableRateLimiting("register")]
        public async Task<ActionResult<AuthResponseDto>> Register(RegisterDto dto)
        {
            var username = dto.Username.Trim().ToLowerInvariant();

            if (await _db.Users.AnyAsync(u => u.Username == username))
                return Conflict(new { message = "That username is already taken." });

            if (await _db.Users.AnyAsync(u => u.Email == dto.Email))
                return Conflict(new { message = "An account with this email already exists." });

            if (await _db.Users.AnyAsync(u => u.PhoneNumber == dto.PhoneNumber))
                return Conflict(new { message = "An account with this phone number already exists." });

            var user = new User
            {
                Username = username,
                FullName = dto.FullName,
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                PhoneNumber = dto.PhoneNumber,
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
                PhotoUrl = user.PhotoUrl,
                NotifyOnGroupMessages = user.NotifyOnGroupMessages,
                NotifyOnDirectMessages = user.NotifyOnDirectMessages
            });
        }

        [HttpPost("login")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
        {
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

        // PATCH /api/auth/change-phone -- same current-password gate as ChangeEmail above.
        [Authorize(Roles = "Student")]
        [HttpPatch("change-phone")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<ChangePhoneResponseDto>> ChangePhone(ChangePhoneDto dto)
        {
            var user = await _db.Users.FindAsync(User.GetUserId());
            if (user == null) return NotFound();

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                return BadRequest(new { message = "Current password is incorrect." });

            var newPhoneNumber = dto.NewPhoneNumber.Trim();
            if (await _db.Users.AnyAsync(u => u.Id != user.Id && u.PhoneNumber == newPhoneNumber))
                return Conflict(new { message = "An account with this phone number already exists." });

            user.PhoneNumber = newPhoneNumber;
            await _db.SaveChangesAsync();

            return Ok(new ChangePhoneResponseDto { PhoneNumber = user.PhoneNumber });
        }
    }
}
