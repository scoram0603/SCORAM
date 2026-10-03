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
    // USER FEEDBACK -- students send suggestions / improvements / bug reports from the floating
    // Feedback button on Home (web + Flutter); admins read and triage them under Admin > Feedback.
    //
    //   Student:  POST  /api/feedback                      (Student only, user id from the token)
    //   Admin:    GET   /api/admin/feedback                (list, search + filters + paging)
    //             GET   /api/admin/feedback/{id}           (detail)
    //             PATCH /api/admin/feedback/{id}/status    (New -> In Review -> Resolved / Rejected)
    // Admin endpoints require the ManageFeedback permission (SuperAdmin always has it).
    [ApiController]
    [Route("api")]
    public class FeedbackController : ControllerBase
    {
        private const int MaxMessageLength = 2000;
        private const int PreviewLength = 120;

        // A byte-for-byte repeat of the same feedback from the same student inside this window is
        // treated as an accidental double submit (double tap / retry after a slow response) and
        // answered with the ORIGINAL record instead of creating a duplicate. The client also disables
        // its Submit button while a request is in flight -- this is the server-side backstop.
        private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(60);

        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IAuditLogService _audit;

        public FeedbackController(ScoramDbContext db, IAdminPermissionService permissions, IAuditLogService audit)
        {
            _db = db;
            _permissions = permissions;
            _audit = audit;
        }

        // ---------------------------------------------------------------- student

        // POST /api/feedback
        [HttpPost("feedback")]
        [Authorize(Roles = "Student")]
        [EnableRateLimiting("feedback")]
        public async Task<ActionResult<FeedbackSubmittedDto>> Submit(FeedbackCreateDto dto)
        {
            if (!TryParseEnum<FeedbackType>(dto.FeedbackType, out var type))
                return BadRequest(new { message = "Please choose a feedback type." });

            var message = (dto.Message ?? string.Empty).Trim();
            if (message.Length == 0)
                return BadRequest(new { message = "Please write your feedback." });
            if (message.Length > MaxMessageLength)
                return BadRequest(new { message = $"Feedback can be at most {MaxMessageLength} characters." });

            if (dto.Rating.HasValue && (dto.Rating.Value < 1 || dto.Rating.Value > 5))
                return BadRequest(new { message = "Rating must be between 1 and 5." });

            // Unknown/missing platform is stored as Other rather than rejected -- never lose feedback
            // over a metadata field.
            var platform = TryParseEnum<FeedbackPlatform>(dto.Platform, out var parsedPlatform)
                ? parsedPlatform
                : FeedbackPlatform.Other;

            var source = string.IsNullOrWhiteSpace(dto.Source) ? null : dto.Source.Trim();
            if (source != null && source.Length > 100) source = source.Substring(0, 100);

            var userId = User.GetUserId();

            var since = DateTime.UtcNow - DuplicateWindow;
            var duplicate = await _db.UserFeedbacks
                .Where(f => f.UserId == userId && f.FeedbackType == type && f.Message == message && f.CreatedAt >= since)
                .OrderByDescending(f => f.CreatedAt)
                .FirstOrDefaultAsync();
            if (duplicate != null) return Ok(ToSubmittedDto(duplicate));

            var now = DateTime.UtcNow;
            var feedback = new UserFeedback
            {
                UserId = userId,
                FeedbackType = type,
                Message = message,
                Rating = dto.Rating,
                Source = source,
                Platform = platform,
                Status = FeedbackStatus.New,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.UserFeedbacks.Add(feedback);
            await _db.SaveChangesAsync();

            return Ok(ToSubmittedDto(feedback));
        }

        // ---------------------------------------------------------------- admin

        // GET /api/admin/feedback?search=&type=&status=&platform=&from=&to=&page=&pageSize=
        [HttpGet("admin/feedback")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<PagedResult<AdminFeedbackDto>>> AdminList(
            [FromQuery] string? search, [FromQuery] string? type, [FromQuery] string? status,
            [FromQuery] string? platform, [FromQuery(Name = "from")] DateTime? fromDate, [FromQuery(Name = "to")] DateTime? toDate,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageFeedback))
                return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.UserFeedbacks.Include(f => f.User).AsQueryable();

            if (!string.IsNullOrWhiteSpace(type))
            {
                if (!TryParseEnum<FeedbackType>(type, out var typeFilter))
                    return BadRequest(new { message = "Unknown feedback type filter." });
                query = query.Where(f => f.FeedbackType == typeFilter);
            }
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!TryParseEnum<FeedbackStatus>(status, out var statusFilter))
                    return BadRequest(new { message = "Unknown status filter." });
                query = query.Where(f => f.Status == statusFilter);
            }
            if (!string.IsNullOrWhiteSpace(platform))
            {
                if (!TryParseEnum<FeedbackPlatform>(platform, out var platformFilter))
                    return BadRequest(new { message = "Unknown platform filter." });
                query = query.Where(f => f.Platform == platformFilter);
            }
            if (fromDate.HasValue) query = query.Where(f => f.CreatedAt >= fromDate.Value.Date);
            // `to` is a date -- include that whole day.
            if (toDate.HasValue) query = query.Where(f => f.CreatedAt < toDate.Value.Date.AddDays(1));

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                if (term.Length > 100) term = term.Substring(0, 100);
                var pattern = "%" + term.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";

                // "FDB-42" / "fdb42" / "42" all find feedback number 42.
                var digits = new string(term.Where(char.IsDigit).ToArray());
                var looksLikeCode = digits.Length > 0
                    && digits.Length <= 9
                    && (term.StartsWith("FDB", StringComparison.OrdinalIgnoreCase) || digits.Length == term.Length);
                var number = looksLikeCode ? int.Parse(digits) : -1;

                query = query.Where(f =>
                    EF.Functions.Like(f.Message, pattern)
                    || EF.Functions.Like(f.User!.FullName, pattern)
                    || EF.Functions.Like(f.User!.Username, pattern)
                    || f.FeedbackNumber == number);
            }

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(f => f.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new PagedResult<AdminFeedbackDto>
            {
                Items = items.Select(ToAdminDto).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        // GET /api/admin/feedback/{id}
        [HttpGet("admin/feedback/{id:guid}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<AdminFeedbackDto>> AdminGet(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageFeedback))
                return Forbid();

            var feedback = await _db.UserFeedbacks.Include(f => f.User).FirstOrDefaultAsync(f => f.Id == id);
            if (feedback == null) return NotFound(new { message = "Feedback not found." });
            return Ok(ToAdminDto(feedback));
        }

        // PATCH /api/admin/feedback/{id}/status  { "status": "New" | "InReview" | "Resolved" | "Rejected" }
        [HttpPatch("admin/feedback/{id:guid}/status")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public async Task<ActionResult<AdminFeedbackDto>> AdminUpdateStatus(Guid id, UpdateFeedbackStatusDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ManageFeedback))
                return Forbid();

            if (!TryParseEnum<FeedbackStatus>(dto.Status, out var newStatus))
                return BadRequest(new { message = $"'{dto.Status}' isn't a valid status." });

            var feedback = await _db.UserFeedbacks.Include(f => f.User).FirstOrDefaultAsync(f => f.Id == id);
            if (feedback == null) return NotFound(new { message = "Feedback not found." });

            if (feedback.Status != newStatus)
            {
                var previous = feedback.Status;
                feedback.Status = newStatus;
                feedback.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                await _audit.LogAsync(User.GetAdminId(), $"Feedback.{newStatus}", "UserFeedback", feedback.Id,
                    $"{ToCode(feedback.FeedbackNumber)}: {previous} -> {newStatus}");
            }

            return Ok(ToAdminDto(feedback));
        }

        // ---------------------------------------------------------------- helpers

        // Enum.TryParse accepts bare numbers ("7") and unknown combos -- only accept real, defined names.
        private static bool TryParseEnum<T>(string? raw, out T value) where T : struct, Enum
        {
            value = default;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return Enum.TryParse(raw.Trim(), ignoreCase: true, out value) && Enum.IsDefined(typeof(T), value);
        }

        private static string ToCode(int number) => $"FDB-{number:D5}";

        private static FeedbackSubmittedDto ToSubmittedDto(UserFeedback f) => new FeedbackSubmittedDto
        {
            Id = f.Id,
            FeedbackCode = ToCode(f.FeedbackNumber),
            Status = f.Status.ToString(),
            CreatedAt = f.CreatedAt
        };

        private static AdminFeedbackDto ToAdminDto(UserFeedback f) => new AdminFeedbackDto
        {
            Id = f.Id,
            FeedbackCode = ToCode(f.FeedbackNumber),
            UserFullName = f.User?.FullName ?? "Unknown user",
            Username = f.User?.Username,
            FeedbackType = f.FeedbackType.ToString(),
            Message = f.Message,
            MessagePreview = f.Message.Length <= PreviewLength ? f.Message : f.Message.Substring(0, PreviewLength).TrimEnd() + "…",
            Rating = f.Rating,
            Platform = f.Platform.ToString(),
            Source = f.Source,
            Status = f.Status.ToString(),
            CreatedAt = f.CreatedAt,
            UpdatedAt = f.UpdatedAt
        };
    }
}
