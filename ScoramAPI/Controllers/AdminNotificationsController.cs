using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    // ADMIN BROADCAST -- React Admin -> this API -> NotificationFanOutService -> NotificationService ->
    // FCM. Neither the admin frontend nor the mobile app ever talks to Firebase or holds its
    // credentials.
    //
    // Permission: reuses AdminPermission.PostNotices ("an admin trusted to post exam notices" -- a
    // student announcement is the same trust level; the SuperAdmin holds every permission by default,
    // see AdminPermissionService). A dedicated permission would also need the admin permission-matrix
    // UI updated, which was out of scope here.
    //
    // Routing is CONTROLLED: an admin picks a whitelisted Destination key; the server -- not the admin
    // -- decides the stored web path, and the mobile app only honours the same whitelist. A free-form
    // route typed by an admin is never accepted or forwarded.
    [ApiController]
    [Route("api/admin/notifications")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AdminNotificationsController : ControllerBase
    {
        private const int MaxTargetedUsers = 1000;

        // Destination key -> web path stored in Notification.LinkUrl. The same keys are understood by
        // the Flutter NotificationRouter (EntityType "Screen").
        private static readonly Dictionary<string, string> Destinations = new(StringComparer.OrdinalIgnoreCase)
        {
            // NOTICE FIX: these used to be "/notifications", "/mock-tests" and "/papers" -- none of which
            // exist as website routes (the web app uses /tests/mock and /pyq, and has no /notifications
            // page), so clicking a notice in the web bell landed on a 404. They now point at real web
            // routes. Rows already stored with the old paths are mapped by the website from EntityId.
            ["Home"] = "/",
            ["Notifications"] = "/",
            ["Quizzes"] = "/quizzes",
            ["MockTests"] = "/tests/mock",
            ["Pyp"] = "/pyq",
            ["Tests"] = "/tests",
            ["Progress"] = "/progress"
        };

        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IAuditLogService _audit;
        private readonly INotificationFanOutService _fanOut;

        public AdminNotificationsController(ScoramDbContext db, IAdminPermissionService permissions,
            IAuditLogService audit, INotificationFanOutService fanOut)
        {
            _db = db;
            _permissions = permissions;
            _audit = audit;
            _fanOut = fanOut;
        }

        // POST /api/admin/notifications/announce
        // 202 Accepted: delivery runs in the background; there is no per-recipient delivery status yet
        // (the in-process queue doesn't persist one) -- see NotificationWorkQueue's comment.
        [HttpPost("announce")]
        public async Task<IActionResult> Announce(SendAnnouncementDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.PostNotices)) return Forbid();

            var title = dto.Title?.Trim() ?? string.Empty;
            var body = dto.Body?.Trim() ?? string.Empty;
            if (title.Length == 0 || body.Length == 0)
                return BadRequest(new { message = "Title and message are required." });

            if (!Destinations.TryGetValue(dto.Destination ?? "Notifications", out var linkUrl))
                return BadRequest(new { message = $"Destination must be one of: {string.Join(", ", Destinations.Keys)}." });
            var destinationKey = Destinations.Keys.First(k => k.Equals(dto.Destination ?? "Notifications", StringComparison.OrdinalIgnoreCase));

            var announcementId = Guid.NewGuid();
            FanOutAudience audience;
            var examIds = new List<Guid>();
            var userIds = new List<Guid>();

            switch ((dto.Target ?? "AllStudents").Trim().ToLowerInvariant())
            {
                case "allstudents":
                    audience = FanOutAudience.AllStudents;
                    break;
                case "exams":
                    audience = FanOutAudience.ExamAudience;
                    examIds = (dto.ExamIds ?? new()).Distinct().ToList();
                    if (examIds.Count == 0) return BadRequest(new { message = "Pick at least one exam." });
                    var found = await _db.Exams.CountAsync(e => examIds.Contains(e.Id));
                    if (found != examIds.Count) return BadRequest(new { message = "One or more selected exams don't exist." });
                    break;
                case "users":
                    audience = FanOutAudience.SpecificUsers;
                    userIds = (dto.UserIds ?? new()).Distinct().ToList();
                    if (userIds.Count == 0) return BadRequest(new { message = "Pick at least one user." });
                    if (userIds.Count > MaxTargetedUsers)
                        return BadRequest(new { message = $"You can target at most {MaxTargetedUsers} users at once." });
                    break;
                default:
                    return BadRequest(new { message = "Target must be AllStudents, Exams or Users." });
            }

            _fanOut.Queue(new FanOutRequest
            {
                Audience = audience,
                ExamIds = examIds,
                UserIds = userIds,
                Type = NotificationType.SystemAnnouncement,
                Title = title,
                Body = body,
                LinkUrl = linkUrl,
                EntityType = "Screen",
                EntityId = destinationKey,
                DedupKey = $"Announcement:{announcementId}"
            });

            await _audit.LogAsync(User.GetAdminId(), "Notification.Announce", "Notification", announcementId);
            return Accepted(new { id = announcementId, target = audience.ToString() });
        }

        // POST /api/admin/notifications/audience-preview -- "how many students will this reach?" before sending.
        // Same audience rules as Announce / NotificationFanOutService (active students only).
        [HttpPost("audience-preview")]
        public async Task<IActionResult> AudiencePreview(AudiencePreviewDto dto)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.PostNotices)) return Forbid();

            switch ((dto.Target ?? "AllStudents").Trim().ToLowerInvariant())
            {
                case "allstudents":
                    return Ok(new { recipients = await _db.Users.CountAsync(u => u.IsActive) });

                case "exams":
                {
                    var examIds = (dto.ExamIds ?? new()).Distinct().ToList();
                    if (examIds.Count == 0) return Ok(new { recipients = 0 });
                    var count = await _db.UserExamPreferences
                        .Where(p => examIds.Contains(p.ExamId) && p.User!.IsActive)
                        .Select(p => p.UserId).Distinct().CountAsync();
                    return Ok(new { recipients = count });
                }

                case "users":
                {
                    var userIds = (dto.UserIds ?? new()).Distinct().ToList();
                    if (userIds.Count > MaxTargetedUsers)
                        return BadRequest(new { message = $"You can target at most {MaxTargetedUsers} users at once." });
                    var count = await _db.Users.CountAsync(u => userIds.Contains(u.Id) && u.IsActive);
                    return Ok(new { recipients = count });
                }

                default:
                    return BadRequest(new { message = "Target must be AllStudents, Exams or Users." });
            }
        }

        // GET /api/admin/notifications/find-users?q=  -- tiny lookup for "specific students" targeting.
        // Returns id, username and name only (no contact details), at most 10, minimum 2 characters.
        [HttpGet("find-users")]
        public async Task<IActionResult> FindUsers([FromQuery] string q)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.PostNotices)) return Forbid();

            var term = (q ?? string.Empty).Trim();
            if (term.Length < 2) return Ok(Array.Empty<object>());
            if (term.Length > 50) term = term.Substring(0, 50);
            var pattern = "%" + term.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";

            var rows = await _db.Users.AsNoTracking()
                .Where(u => u.IsActive && (EF.Functions.Like(u.Username, pattern) || EF.Functions.Like(u.FullName, pattern)))
                .OrderBy(u => u.Username).Take(10)
                .Select(u => new { u.Id, u.Username, u.FullName })
                .ToListAsync();
            return Ok(rows);
        }

        // GET /api/admin/notifications/campaigns?page=&pageSize=  -- history of admin announcements.
        // A "campaign" is every Notification row sharing the announcement's DedupKey. Measurable per campaign:
        // recipients (rows created) and in-app reads. Push DELIVERY results and clicks are not recorded by
        // the platform, so they are not shown (and never estimated).
        [HttpGet("campaigns")]
        public async Task<IActionResult> Campaigns([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await _permissions.HasPermissionAsync(User, AdminPermission.PostNotices)) return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var baseQuery = _db.Notifications
                .Where(n => n.Type == NotificationType.SystemAnnouncement && n.DedupKey != null);

            var total = await baseQuery.Select(n => n.DedupKey).Distinct().CountAsync();

            var rows = await baseQuery
                .GroupBy(n => n.DedupKey)
                .Select(g => new
                {
                    Key = g.Key,
                    Title = g.Min(n => n.Title),
                    Body = g.Min(n => n.Body),
                    SentAt = g.Min(n => n.CreatedAt),
                    Recipients = g.Count(),
                    Read = g.Count(n => n.IsRead)
                })
                .OrderByDescending(x => x.SentAt)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync();

            // Who sent each one: the audit entry written by Announce (TargetId = the announcement id).
            var ids = rows
                .Select(r => r.Key != null && r.Key.StartsWith("Announcement:") && Guid.TryParse(r.Key.Substring("Announcement:".Length), out var g) ? g : (Guid?)null)
                .Where(g => g != null).Select(g => g!.Value).ToList();
            var senders = await _db.AuditLogs
                .Where(a => a.Action == "Notification.Announce" && a.TargetId != null && ids.Contains(a.TargetId.Value))
                .Select(a => new { a.TargetId, Admin = a.Admin != null ? a.Admin.FullName : "Unknown" })
                .ToListAsync();
            var senderMap = senders.Where(x => x.TargetId != null).GroupBy(x => x.TargetId!.Value).ToDictionary(g => g.Key, g => g.First().Admin);

            return Ok(new
            {
                items = rows.Select(r =>
                {
                    Guid? campaignId = r.Key != null && r.Key.StartsWith("Announcement:") && Guid.TryParse(r.Key.Substring("Announcement:".Length), out var g) ? g : null;
                    return new
                    {
                        id = r.Key,
                        title = r.Title,
                        body = r.Body,
                        sentAt = r.SentAt,
                        sentBy = campaignId != null && senderMap.TryGetValue(campaignId.Value, out var who) ? who : null,
                        recipients = r.Recipients,
                        read = r.Read,
                        readRate = r.Recipients == 0 ? 0 : Math.Round(r.Read * 100.0 / r.Recipients, 1)
                    };
                }),
                totalCount = total,
                page,
                pageSize,
                note = "Delivered / failed / clicked counts are not available: the platform records recipients and in-app reads only."
            });
        }
    }
}
