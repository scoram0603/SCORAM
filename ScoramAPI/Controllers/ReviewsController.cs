using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // Invalidation token for the short public caches below. Bumped whenever a review changes state, so an
    // approval shows up on the landing page on the next request (this instance) or within the cache TTL
    // (other instances) -- never needs a frontend redeploy.
    public static class PublicContentCache
    {
        private static int _reviewsVersion;
        private static int _landingVersion;
        public static int ReviewsVersion => Volatile.Read(ref _reviewsVersion);
        public static int LandingVersion => Volatile.Read(ref _landingVersion);
        public static void BumpReviews() => Interlocked.Increment(ref _reviewsVersion);
        public static void BumpLanding() => Interlocked.Increment(ref _landingVersion);
    }

    // STUDENT REVIEWS -- student side (own review only).
    //   GET    /api/reviews/mine   -> { review: {...} | null }
    //   PUT    /api/reviews/mine   -> create or edit the caller's single review (edits go back to Pending)
    //   DELETE /api/reviews/mine   -> withdraw (permanently delete) the caller's own review
    // The owner is ALWAYS the authenticated user from the token -- no endpoint accepts a user id.
    [ApiController]
    [Route("api/reviews")]
    [Authorize(Roles = "Student")]
    public class ReviewsController : ControllerBase
    {
        private readonly ScoramDbContext _db;

        public ReviewsController(ScoramDbContext db) => _db = db;

        [HttpGet("mine")]
        public async Task<ActionResult<MyReviewResponseDto>> GetMine()
        {
            var userId = User.GetUserId();
            var review = await _db.StudentReviews.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId);
            return Ok(new MyReviewResponseDto { Review = review == null ? null : ToMyDto(review) });
        }

        [HttpPut("mine")]
        [EnableRateLimiting("review-write")]
        public async Task<ActionResult<MyReviewDto>> UpsertMine(UpsertReviewDto dto)
        {
            var (text, error) = ReviewRules.Validate(dto.Rating, dto.Text);
            if (error != null) return BadRequest(new { message = error });

            var userId = User.GetUserId();
            var now = DateTime.UtcNow;

            var existing = await _db.StudentReviews.FirstOrDefaultAsync(r => r.UserId == userId);
            if (existing == null)
            {
                var created = new StudentReview
                {
                    UserId = userId,
                    Rating = dto.Rating!.Value,
                    Text = text!,
                    ShowNamePublicly = dto.ShowNamePublicly,
                    Status = ReviewStatus.Pending,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.StudentReviews.Add(created);
                try
                {
                    await _db.SaveChangesAsync();
                    PublicContentCache.BumpReviews();
                    return Ok(ToMyDto(created));
                }
                catch (DbUpdateException)
                {
                    // Two quick taps raced past the "no review yet" check; the unique index on UserId let one
                    // through. Fall through and treat the winner as the existing review (latest edit wins).
                    _db.Entry(created).State = EntityState.Detached;
                    existing = await _db.StudentReviews.FirstOrDefaultAsync(r => r.UserId == userId);
                    if (existing == null) throw;
                }
            }

            // A moderator pulled this one from public view: editing must not be a way to re-publish it.
            if (existing.Status == ReviewStatus.Hidden)
                return Conflict(new { message = "This review was removed from public view by SCORAM moderators, so it can't be edited. You can withdraw it and write a new one." });

            var unchanged = existing.Rating == dto.Rating!.Value
                            && existing.Text == text
                            && existing.ShowNamePublicly == dto.ShowNamePublicly;
            if (!unchanged)
            {
                existing.Rating = dto.Rating.Value;
                existing.Text = text!;
                existing.ShowNamePublicly = dto.ShowNamePublicly;
                // Any edit needs a fresh moderation decision -- an approved review must not be swappable for
                // different content that was never looked at.
                existing.Status = ReviewStatus.Pending;
                existing.UpdatedAt = now;
                await _db.SaveChangesAsync();
                PublicContentCache.BumpReviews();
            }

            return Ok(ToMyDto(existing));
        }

        [HttpDelete("mine")]
        [EnableRateLimiting("review-write")]
        public async Task<IActionResult> WithdrawMine()
        {
            var userId = User.GetUserId();
            var existing = await _db.StudentReviews.FirstOrDefaultAsync(r => r.UserId == userId);
            if (existing == null) return NoContent();

            _db.StudentReviews.Remove(existing);
            await _db.SaveChangesAsync();
            PublicContentCache.BumpReviews();
            return NoContent();
        }

        private static MyReviewDto ToMyDto(StudentReview r) => new()
        {
            Id = r.Id,
            Rating = r.Rating,
            Text = r.Text,
            ShowNamePublicly = r.ShowNamePublicly,
            Status = r.Status.ToString(),
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        };
    }

    // STUDENT REVIEWS -- PUBLIC listing used by the landing page. Anonymous, rate limited per IP, paginated
    // and briefly cached. The visibility rule is applied HERE in the query (Approved + rating >= 3 + active
    // author); the web client's own filtering is only a presentation safeguard.
    [ApiController]
    [Route("api/public/reviews")]
    [AllowAnonymous]
    public class PublicReviewsController : ControllerBase
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

        private readonly ScoramDbContext _db;
        private readonly IMemoryCache _cache;

        public PublicReviewsController(ScoramDbContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        // GET /api/public/reviews?page=1&pageSize=6  -> { items, totalCount, page, pageSize }
        [HttpGet]
        [EnableRateLimiting("public-read")]
        public async Task<ActionResult<PagedResult<PublicReviewDto>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 6)
        {
            page = ReviewRules.ClampPage(page);
            pageSize = ReviewRules.ClampPageSize(pageSize);

            var cacheKey = $"public-reviews:v{PublicContentCache.ReviewsVersion}:{page}:{pageSize}";
            if (!_cache.TryGetValue(cacheKey, out PagedResult<PublicReviewDto>? result) || result == null)
            {
                var eligible = _db.StudentReviews.AsNoTracking()
                    .Where(r => r.Status == ReviewStatus.Approved
                                && r.Rating >= ReviewRules.MinPublicRating
                                && r.User!.IsActive);

                var total = await eligible.CountAsync();
                var rows = await eligible
                    .OrderByDescending(r => r.UpdatedAt)
                    .ThenBy(r => r.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(r => new { r.Id, r.Rating, r.Text, r.ShowNamePublicly, FullName = r.User!.FullName, r.CreatedAt })
                    .ToListAsync();

                result = new PagedResult<PublicReviewDto>
                {
                    TotalCount = total,
                    Page = page,
                    PageSize = pageSize,
                    Items = rows.Select(r => new PublicReviewDto
                    {
                        Id = r.Id,
                        Rating = r.Rating,
                        Text = r.Text,
                        DisplayName = ReviewRules.PublicDisplayName(r.FullName, r.ShowNamePublicly),
                        CreatedAt = r.CreatedAt
                    }).ToList()
                };
                _cache.Set(cacheKey, result, CacheTtl);
            }

            Response.Headers.CacheControl = "public, max-age=60";
            return Ok(result);
        }
    }

    // STUDENT REVIEWS -- admin moderation (Admin > Student Reviews). Needs ModerateReviews (SuperAdmin implicit);
    // every state change is written to the audit log (action + review id + rating only, never the text).
    //   GET    /api/admin/reviews?status=&rating=&search=&page=&pageSize=
    //   PATCH  /api/admin/reviews/{id}/status   { status: Pending|Approved|Rejected|Hidden, note }
    //   DELETE /api/admin/reviews/{id}          (removes only that review row -- never the student's account)
    [ApiController]
    [Route("api/admin/reviews")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AdminReviewsController : ControllerBase
    {
        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IAuditLogService _audit;

        public AdminReviewsController(ScoramDbContext db, IAdminPermissionService permissions, IAuditLogService audit)
        {
            _db = db;
            _permissions = permissions;
            _audit = audit;
        }

        [HttpGet]
        public async Task<ActionResult<AdminReviewListDto>> List(
            [FromQuery] string? status, [FromQuery] int? rating, [FromQuery] string? search,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateReviews)) return Forbid();

            page = ReviewRules.ClampPage(page);
            pageSize = ReviewRules.ClampPageSize(pageSize, 100);

            var query = _db.StudentReviews.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!TryParseStatus(status, out var parsed))
                    return BadRequest(new { message = $"'{status}' isn't a valid status." });
                query = query.Where(r => r.Status == parsed);
            }
            if (rating.HasValue)
            {
                if (rating < ReviewRules.MinRating || rating > ReviewRules.MaxRating)
                    return BadRequest(new { message = "rating must be between 1 and 5." });
                query = query.Where(r => r.Rating == rating.Value);
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                if (term.Length > 100) term = term.Substring(0, 100);
                query = query.Where(r => r.Text.Contains(term) || r.User!.FullName.Contains(term) || r.User!.Username.Contains(term));
            }

            var total = await query.CountAsync();
            var rows = await query
                .OrderBy(r => r.Status == ReviewStatus.Pending ? 0 : 1) // moderation queue first
                .ThenByDescending(r => r.UpdatedAt)
                .ThenBy(r => r.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new
                {
                    r.Id, r.Rating, r.Text, r.Status, r.ShowNamePublicly, r.CreatedAt, r.UpdatedAt,
                    r.ModeratedAt, r.ModeratedByAdminId, r.ModerationNote,
                    FullName = r.User!.FullName, Username = r.User!.Username, Active = r.User!.IsActive
                })
                .ToListAsync();

            // One lookup for all moderator names on the page (no N+1).
            var adminIds = rows.Where(r => r.ModeratedByAdminId != null).Select(r => r.ModeratedByAdminId!.Value).Distinct().ToList();
            var adminNames = adminIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await _db.Admins.Where(a => adminIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.FullName);

            // Summary over ALL reviews (not just the filtered page) so the header counts are stable.
            var all = _db.StudentReviews.AsNoTracking();
            var summary = new AdminReviewSummaryDto
            {
                Total = await all.CountAsync(),
                Pending = await all.CountAsync(r => r.Status == ReviewStatus.Pending),
                Approved = await all.CountAsync(r => r.Status == ReviewStatus.Approved),
                Rejected = await all.CountAsync(r => r.Status == ReviewStatus.Rejected),
                Hidden = await all.CountAsync(r => r.Status == ReviewStatus.Hidden),
                AverageRatingAll = await all.AnyAsync() ? Math.Round(await all.AverageAsync(r => (double)r.Rating), 2) : null,
                PublicOnLanding = await all.CountAsync(r => r.Status == ReviewStatus.Approved
                                                            && r.Rating >= ReviewRules.MinPublicRating
                                                            && r.User!.IsActive)
            };

            return Ok(new AdminReviewListDto
            {
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
                Summary = summary,
                Items = rows.Select(r => new AdminReviewDto
                {
                    Id = r.Id,
                    Rating = r.Rating,
                    Text = r.Text,
                    Status = r.Status.ToString(),
                    ShowNamePublicly = r.ShowNamePublicly,
                    PublicName = ReviewRules.PublicDisplayName(r.FullName, r.ShowNamePublicly),
                    AuthorFullName = r.FullName,
                    AuthorUsername = r.Username,
                    AuthorActive = r.Active,
                    PublicOnLanding = ReviewRules.IsPubliclyEligible(r.Status, r.Rating, r.Active),
                    CreatedAt = r.CreatedAt,
                    UpdatedAt = r.UpdatedAt,
                    ModeratedAt = r.ModeratedAt,
                    ModeratedBy = r.ModeratedByAdminId != null && adminNames.TryGetValue(r.ModeratedByAdminId.Value, out var n) ? n : null,
                    ModerationNote = r.ModerationNote
                }).ToList()
            });
        }

        [HttpPatch("{id:guid}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, UpdateReviewStatusDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateReviews)) return Forbid();

            if (!TryParseStatus(dto.Status, out var newStatus))
                return BadRequest(new { message = $"'{dto.Status}' isn't a valid status." });

            var note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            if (note != null && note.Length > 500) return BadRequest(new { message = "The note can be at most 500 characters." });

            var review = await _db.StudentReviews.FirstOrDefaultAsync(r => r.Id == id);
            if (review == null) return NotFound(new { message = "Review not found." });

            var previous = review.Status;
            var adminId = User.GetAdminId();
            review.Status = newStatus;
            review.ModeratedAt = DateTime.UtcNow;
            review.ModeratedByAdminId = adminId;
            review.ModerationNote = note;
            await _db.SaveChangesAsync();
            PublicContentCache.BumpReviews();

            await _audit.LogAsync(adminId, $"Review.{newStatus}", "StudentReview", review.Id,
                $"{previous} -> {newStatus}; rating {review.Rating}");

            return Ok(new { id = review.Id, status = review.Status.ToString() });
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ModerateReviews)) return Forbid();

            var review = await _db.StudentReviews.FirstOrDefaultAsync(r => r.Id == id);
            if (review == null) return NotFound(new { message = "Review not found." });

            var rating = review.Rating;
            _db.StudentReviews.Remove(review);
            await _db.SaveChangesAsync();
            PublicContentCache.BumpReviews();

            await _audit.LogAsync(User.GetAdminId(), "Review.Deleted", "StudentReview", id, $"rating {rating}");
            return NoContent();
        }

        // Enum.TryParse accepts bare numbers ("7") -- only accept real, defined names.
        private static bool TryParseStatus(string? raw, out ReviewStatus value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return Enum.TryParse(raw.Trim(), ignoreCase: true, out value) && Enum.IsDefined(typeof(ReviewStatus), value);
        }
    }
}
