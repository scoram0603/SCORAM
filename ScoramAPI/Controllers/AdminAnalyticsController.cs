using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // ADMIN ANALYTICS -- backend for the redesigned dashboard and the Analytics page. All numbers come from
    // AdminAnalyticsService (one definition per metric, shared with the report exports). Permission rules:
    //   overview        any admin; user / learning KPIs only appear for ViewAnalytics holders (others get
    //                   the content + moderation KPIs the original dashboard already showed everyone)
    //   users, content, learning, community   ViewAnalytics
    //   operations      Audit  (it summarises who did what)
    //   badges          any admin; each count only returned for queues the admin can actually open
    // Common query: from=yyyy-MM-dd&to=yyyy-MM-dd (admin-local, inclusive), tzOffsetMinutes (default 330),
    // granularity=day|week|month, refresh=true to bypass the 60-second cache.
    [ApiController]
    [Route("api/admin/analytics")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AdminAnalyticsController : ControllerBase
    {
        private readonly IAdminAnalyticsService _analytics;
        private readonly IAdminPermissionService _permissions;
        private readonly ScoramDbContext _db;

        public AdminAnalyticsController(IAdminAnalyticsService analytics, IAdminPermissionService permissions, ScoramDbContext db)
        {
            _analytics = analytics;
            _permissions = permissions;
            _db = db;
        }

        private AnalyticsRange Range(string? from, string? to, int? tzOffsetMinutes, string? granularity, bool refresh)
        {
            if (refresh) _analytics.InvalidateCache();
            return _analytics.ResolveRange(from, to, tzOffsetMinutes, granularity);
        }

        [HttpGet("overview")]
        public async Task<ActionResult<AnalyticsOverviewDto>> Overview(
            [FromQuery] string? from, [FromQuery] string? to, [FromQuery] int? tzOffsetMinutes = null,
            [FromQuery] bool refresh = false, CancellationToken ct = default)
        {
            var canViewAnalytics = await _permissions.HasPermissionAsync(User, AdminPermission.ViewAnalytics);
            var range = Range(from, to, tzOffsetMinutes, "day", refresh);
            return Ok(await _analytics.GetOverviewAsync(range, canViewAnalytics, ct));
        }

        [HttpGet("users")]
        public async Task<ActionResult<AnalyticsUsersDto>> Users(
            [FromQuery] string? from, [FromQuery] string? to, [FromQuery] int? tzOffsetMinutes = null,
            [FromQuery] string? granularity = null, [FromQuery] bool refresh = false, CancellationToken ct = default)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ViewAnalytics)) return Forbid();
            return Ok(await _analytics.GetUsersAsync(Range(from, to, tzOffsetMinutes, granularity, refresh), ct));
        }

        [HttpGet("content")]
        public async Task<ActionResult<AnalyticsContentDto>> Content(
            [FromQuery] string? from, [FromQuery] string? to, [FromQuery] int? tzOffsetMinutes = null,
            [FromQuery] string? granularity = null, [FromQuery] bool refresh = false, CancellationToken ct = default)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ViewAnalytics)) return Forbid();
            return Ok(await _analytics.GetContentAsync(Range(from, to, tzOffsetMinutes, granularity, refresh), ct));
        }

        [HttpGet("learning")]
        public async Task<ActionResult<AnalyticsLearningDto>> Learning(
            [FromQuery] string? from, [FromQuery] string? to, [FromQuery] int? tzOffsetMinutes = null,
            [FromQuery] string? granularity = null, [FromQuery] bool refresh = false, CancellationToken ct = default)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ViewAnalytics)) return Forbid();
            return Ok(await _analytics.GetLearningAsync(Range(from, to, tzOffsetMinutes, granularity, refresh), ct));
        }

        [HttpGet("community")]
        public async Task<ActionResult<AnalyticsCommunityDto>> Community(
            [FromQuery] string? from, [FromQuery] string? to, [FromQuery] int? tzOffsetMinutes = null,
            [FromQuery] string? granularity = null, [FromQuery] bool refresh = false, CancellationToken ct = default)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.ViewAnalytics)) return Forbid();
            return Ok(await _analytics.GetCommunityAsync(Range(from, to, tzOffsetMinutes, granularity, refresh), ct));
        }

        [HttpGet("operations")]
        public async Task<ActionResult<AnalyticsOperationsDto>> Operations(
            [FromQuery] string? from, [FromQuery] string? to, [FromQuery] int? tzOffsetMinutes = null,
            [FromQuery] string? granularity = null, [FromQuery] bool refresh = false, CancellationToken ct = default)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.Audit)) return Forbid();
            return Ok(await _analytics.GetOperationsAsync(Range(from, to, tzOffsetMinutes, granularity, refresh), ct));
        }

        // GET /api/admin/analytics/badges -- the small unresolved-work counters shown beside sidebar items.
        // A count is only included when the caller holds the permission that opens that queue, so the
        // sidebar can never reveal that something exists in a queue the admin has no access to.
        [HttpGet("badges")]
        public async Task<IActionResult> Badges(CancellationToken ct)
        {
            var result = new Dictionary<string, int>();

            if (await _permissions.HasPermissionAsync(User, AdminPermission.PublishPaper))
                result["review"] = await _db.Papers.CountAsync(p => p.Status == PaperStatus.PendingReview, ct);

            if (await _permissions.HasPermissionAsync(User, AdminPermission.ModerateQuestionReports))
                result["questionReports"] = await _db.QuestionReports.CountAsync(x => x.Status == ReportStatus.Pending, ct);

            if (await _permissions.HasPermissionAsync(User, AdminPermission.ModerateSolutions))
                result["solutions"] = await _db.QuestionSolutions.CountAsync(x => !x.IsApproved, ct);

            if (await _permissions.HasPermissionAsync(User, AdminPermission.ModerateDiscussions))
                result["commentReports"] = await _db.CommentReports.CountAsync(x => x.Status == ReportStatus.Pending, ct);

            if (await _permissions.HasPermissionAsync(User, AdminPermission.HandleChatReports))
                result["chatReports"] = await _db.ChatReports.CountAsync(x => x.Status == ChatReportStatus.Pending, ct);

            if (await _permissions.HasPermissionAsync(User, AdminPermission.ManageFeedback))
                result["support"] = await _db.UserFeedbacks.CountAsync(f => f.Status == FeedbackStatus.New || f.Status == FeedbackStatus.InReview, ct);

            return Ok(result);
        }
    }
}
