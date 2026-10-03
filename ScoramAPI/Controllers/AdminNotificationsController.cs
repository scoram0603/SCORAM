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
            ["Home"] = "/",
            ["Notifications"] = "/notifications",
            ["Quizzes"] = "/quizzes",
            ["MockTests"] = "/mock-tests",
            ["Pyp"] = "/papers",
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
    }
}
