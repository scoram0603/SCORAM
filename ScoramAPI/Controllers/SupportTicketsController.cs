using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Enums;
using ScoramAPI.Extensions;
using ScoramAPI.Models;
using ScoramAPI.Services;

namespace ScoramAPI.Controllers
{
    public class TicketUpdateDto
    {
        // Low | Normal | High | Urgent -- omit to leave unchanged.
        public string? Priority { get; set; }
        // "me" | "none" | an admin id -- omit to leave unchanged.
        public string? Assignee { get; set; }
    }

    public class TicketTextDto
    {
        public string? Body { get; set; }
    }

    // SUPPORT & TICKETS -- Admin > Support. This is a workflow layer on top of the EXISTING feedback records
    // (UserFeedback): no record is copied or lost, FeedbackController's student submit endpoint and the
    // original Feedback screen keep working unchanged. What it adds:
    //   priority, assignment to an admin, an internal-notes timeline, and replies that reach the student as an
    //   in-app / push notification (SystemAnnouncement to that one student).
    // Reuses the ManageFeedback permission, so nobody gains access to feedback they could not already open.
    [ApiController]
    [Route("api/admin/support")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class SupportTicketsController : ControllerBase
    {
        private const int PreviewLength = 140;

        private readonly ScoramDbContext _db;
        private readonly IAdminPermissionService _permissions;
        private readonly IAuditLogService _audit;
        private readonly INotificationFanOutService _fanOut;

        public SupportTicketsController(ScoramDbContext db, IAdminPermissionService permissions,
            IAuditLogService audit, INotificationFanOutService fanOut)
        {
            _db = db;
            _permissions = permissions;
            _audit = audit;
            _fanOut = fanOut;
        }

        private Task<bool> CanAsync() => _permissions.HasPermissionAsync(User, AdminPermission.ManageFeedback);
        private static string Code(int number) => $"FDB-{number:D5}";

        private static bool TryEnum<T>(string? raw, out T value) where T : struct, Enum
        {
            value = default;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return Enum.TryParse(raw.Trim(), true, out value) && Enum.IsDefined(typeof(T), value);
        }

        // GET /api/admin/support/agents -- admins a ticket can be assigned to.
        [HttpGet("agents")]
        public async Task<IActionResult> Agents()
        {
            if (!await CanAsync()) return Forbid();

            var agents = await _db.Admins.AsNoTracking()
                .Where(a => a.IsActive && (a.Role == AdminRole.SuperAdmin
                    || a.PermissionGrants.Any(g => g.Permission == AdminPermission.ManageFeedback)))
                .OrderBy(a => a.FullName)
                .Select(a => new { a.Id, a.FullName })
                .ToListAsync();
            return Ok(agents);
        }

        // GET /api/admin/support/tickets?search=&status=&priority=&type=&assignee=me|none|<id>&page=&pageSize=
        [HttpGet("tickets")]
        public async Task<IActionResult> List(
            [FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? priority,
            [FromQuery] string? type, [FromQuery] string? assignee,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (!await CanAsync()) return Forbid();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.UserFeedbacks.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (status.Equals("open", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(f => f.Status == FeedbackStatus.New || f.Status == FeedbackStatus.InReview);
                else if (TryEnum<FeedbackStatus>(status, out var st)) query = query.Where(f => f.Status == st);
                else return BadRequest(new { message = "Unknown status filter." });
            }
            if (!string.IsNullOrWhiteSpace(priority))
            {
                if (!TryEnum<FeedbackPriority>(priority, out var pr)) return BadRequest(new { message = "Unknown priority filter." });
                query = query.Where(f => f.Priority == pr);
            }
            if (!string.IsNullOrWhiteSpace(type))
            {
                if (!TryEnum<FeedbackType>(type, out var ty)) return BadRequest(new { message = "Unknown type filter." });
                query = query.Where(f => f.FeedbackType == ty);
            }
            if (!string.IsNullOrWhiteSpace(assignee))
            {
                if (assignee.Equals("none", StringComparison.OrdinalIgnoreCase)) query = query.Where(f => f.AssignedToAdminId == null);
                else if (assignee.Equals("me", StringComparison.OrdinalIgnoreCase))
                {
                    var me = User.GetAdminId();
                    query = query.Where(f => f.AssignedToAdminId == me);
                }
                else if (Guid.TryParse(assignee, out var gid)) query = query.Where(f => f.AssignedToAdminId == gid);
                else return BadRequest(new { message = "Unknown assignee filter." });
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                if (term.Length > 100) term = term.Substring(0, 100);
                var pattern = "%" + term.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
                var digits = new string(term.Where(char.IsDigit).ToArray());
                var looksLikeCode = digits.Length > 0 && digits.Length <= 9
                    && (term.StartsWith("FDB", StringComparison.OrdinalIgnoreCase) || digits.Length == term.Length);
                var number = looksLikeCode ? int.Parse(digits) : -1;
                query = query.Where(f =>
                    EF.Functions.Like(f.Message, pattern)
                    || EF.Functions.Like(f.User!.Username, pattern)
                    || EF.Functions.Like(f.User!.FullName, pattern)
                    || f.FeedbackNumber == number);
            }

            var total = await query.CountAsync();
            var rows = await query
                // Urgent first among open work, then oldest-first inside a priority so nothing starves.
                .OrderBy(f => f.Status == FeedbackStatus.Resolved || f.Status == FeedbackStatus.Rejected ? 1 : 0)
                .ThenByDescending(f => f.Priority == FeedbackPriority.Urgent ? 3 : f.Priority == FeedbackPriority.High ? 2 : f.Priority == FeedbackPriority.Normal ? 1 : 0)
                .ThenBy(f => f.CreatedAt)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(f => new
                {
                    f.Id, f.FeedbackNumber, f.FeedbackType, f.Message, f.Rating, f.Platform, f.Status, f.Priority,
                    f.CreatedAt, f.UpdatedAt, f.ResolvedAt,
                    Username = f.User != null ? f.User.Username : null,
                    UserFullName = f.User != null ? f.User.FullName : "Unknown user",
                    AssignedTo = f.AssignedToAdmin != null ? f.AssignedToAdmin.FullName : null,
                    AssignedToId = f.AssignedToAdminId,
                    Notes = f.Notes.Count
                })
                .ToListAsync();

            return Ok(new
            {
                items = rows.Select(f => new
                {
                    id = f.Id,
                    code = Code(f.FeedbackNumber),
                    type = f.FeedbackType.ToString(),
                    preview = f.Message.Length <= PreviewLength ? f.Message : f.Message.Substring(0, PreviewLength).TrimEnd() + "…",
                    rating = f.Rating,
                    platform = f.Platform.ToString(),
                    status = f.Status.ToString(),
                    priority = f.Priority.ToString(),
                    username = f.Username,
                    userFullName = f.UserFullName,
                    assignedTo = f.AssignedTo,
                    assignedToId = f.AssignedToId,
                    noteCount = f.Notes,
                    createdAt = f.CreatedAt,
                    updatedAt = f.UpdatedAt,
                    resolvedAt = f.ResolvedAt
                }),
                totalCount = total,
                page,
                pageSize
            });
        }

        // GET /api/admin/support/tickets/{id}
        [HttpGet("tickets/{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            if (!await CanAsync()) return Forbid();

            var f = await _db.UserFeedbacks.AsNoTracking()
                .Include(x => x.User).Include(x => x.AssignedToAdmin)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (f == null) return NotFound(new { message = "Ticket not found." });

            var notes = await _db.FeedbackNotes.AsNoTracking()
                .Where(n => n.FeedbackId == id)
                .OrderBy(n => n.CreatedAt)
                .Select(n => new { n.Id, n.Kind, n.Body, n.CreatedAt, Admin = n.Admin != null ? n.Admin.FullName : "System" })
                .ToListAsync();

            return Ok(new
            {
                id = f.Id,
                code = Code(f.FeedbackNumber),
                type = f.FeedbackType.ToString(),
                message = f.Message,
                rating = f.Rating,
                platform = f.Platform.ToString(),
                source = f.Source,
                status = f.Status.ToString(),
                priority = f.Priority.ToString(),
                createdAt = f.CreatedAt,
                updatedAt = f.UpdatedAt,
                resolvedAt = f.ResolvedAt,
                student = f.User == null ? null : new { id = f.User.Id, username = f.User.Username, fullName = f.User.FullName },
                assignedToId = f.AssignedToAdminId,
                assignedTo = f.AssignedToAdmin?.FullName,
                timeline = notes.Select(n => new { id = n.Id, kind = n.Kind, body = n.Body, at = n.CreatedAt, by = n.Admin })
            });
        }

        // PATCH /api/admin/support/tickets/{id}  { priority?, assignee? }
        [HttpPatch("tickets/{id:guid}")]
        public async Task<IActionResult> Update(Guid id, TicketUpdateDto dto)
        {
            if (!await CanAsync()) return Forbid();

            var f = await _db.UserFeedbacks.FirstOrDefaultAsync(x => x.Id == id);
            if (f == null) return NotFound(new { message = "Ticket not found." });

            var adminId = User.GetAdminId();
            var events = new List<string>();

            if (dto.Priority != null)
            {
                if (!TryEnum<FeedbackPriority>(dto.Priority, out var priority))
                    return BadRequest(new { message = "Priority must be Low, Normal, High or Urgent." });
                if (priority != f.Priority)
                {
                    events.Add($"Priority {f.Priority} -> {priority}");
                    f.Priority = priority;
                }
            }

            if (dto.Assignee != null)
            {
                Guid? newAssignee;
                string newName = "nobody";
                if (dto.Assignee.Equals("none", StringComparison.OrdinalIgnoreCase)) newAssignee = null;
                else if (dto.Assignee.Equals("me", StringComparison.OrdinalIgnoreCase)) newAssignee = adminId;
                else if (Guid.TryParse(dto.Assignee, out var gid)) newAssignee = gid;
                else return BadRequest(new { message = "Assignee must be 'me', 'none' or an admin id." });

                if (newAssignee != null)
                {
                    var target = await _db.Admins.AsNoTracking().FirstOrDefaultAsync(a => a.Id == newAssignee.Value && a.IsActive);
                    if (target == null) return BadRequest(new { message = "That admin does not exist or is deactivated." });
                    newName = target.FullName;

                    // Only admins who can actually work tickets may own one.
                    var canWork = target.Role == AdminRole.SuperAdmin
                        || await _db.AdminPermissionGrants.AnyAsync(g => g.AdminId == target.Id && g.Permission == AdminPermission.ManageFeedback);
                    if (!canWork) return BadRequest(new { message = "That admin does not have the Feedback permission." });
                }

                if (newAssignee != f.AssignedToAdminId)
                {
                    events.Add($"Assigned to {newName}");
                    f.AssignedToAdminId = newAssignee;
                }
            }

            if (events.Count == 0) return Ok(new { changed = false });

            f.UpdatedAt = DateTime.UtcNow;
            foreach (var e in events)
                _db.FeedbackNotes.Add(new FeedbackNote { FeedbackId = f.Id, AdminId = adminId, Kind = "Event", Body = e });
            await _db.SaveChangesAsync();

            await _audit.LogAsync(adminId, "Support.Update", "UserFeedback", f.Id, $"{Code(f.FeedbackNumber)}: {string.Join("; ", events)}");
            return Ok(new { changed = true });
        }

        // POST /api/admin/support/tickets/{id}/notes  { body } -- internal note, never shown to the student.
        [HttpPost("tickets/{id:guid}/notes")]
        public async Task<IActionResult> AddNote(Guid id, TicketTextDto dto)
        {
            if (!await CanAsync()) return Forbid();

            var body = (dto.Body ?? string.Empty).Trim();
            if (body.Length == 0) return BadRequest(new { message = "Write something first." });
            if (body.Length > 2000) return BadRequest(new { message = "Notes can be at most 2000 characters." });

            var f = await _db.UserFeedbacks.FirstOrDefaultAsync(x => x.Id == id);
            if (f == null) return NotFound(new { message = "Ticket not found." });

            _db.FeedbackNotes.Add(new FeedbackNote { FeedbackId = id, AdminId = User.GetAdminId(), Kind = "Note", Body = body });
            f.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new { saved = true });
        }

        // POST /api/admin/support/tickets/{id}/respond  { body } -- reply the STUDENT sees, as an in-app /
        // push notification (SystemAnnouncement to that single student). Recorded on the timeline as a
        // "Response". Duplicate sends are impossible: the notification's idempotency key is the note id.
        [HttpPost("tickets/{id:guid}/respond")]
        public async Task<IActionResult> Respond(Guid id, TicketTextDto dto)
        {
            if (!await CanAsync()) return Forbid();

            var body = (dto.Body ?? string.Empty).Trim();
            if (body.Length == 0) return BadRequest(new { message = "Write your reply first." });
            if (body.Length > 500) return BadRequest(new { message = "Replies can be at most 500 characters." });

            var f = await _db.UserFeedbacks.FirstOrDefaultAsync(x => x.Id == id);
            if (f == null) return NotFound(new { message = "Ticket not found." });

            var note = new FeedbackNote { FeedbackId = id, AdminId = User.GetAdminId(), Kind = "Response", Body = body };
            _db.FeedbackNotes.Add(note);
            f.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _fanOut.Queue(new FanOutRequest
            {
                Audience = FanOutAudience.SpecificUsers,
                UserIds = new List<Guid> { f.UserId },
                Type = NotificationType.SystemAnnouncement,
                Title = $"Reply to your feedback {Code(f.FeedbackNumber)}",
                Body = body,
                LinkUrl = "/notifications",
                EntityType = "Screen",
                EntityId = "Notifications",
                DedupKey = $"FeedbackReply:{note.Id}"
            });

            await _audit.LogAsync(User.GetAdminId(), "Support.Respond", "UserFeedback", f.Id, Code(f.FeedbackNumber));
            return Ok(new { sent = true });
        }
    }
}
