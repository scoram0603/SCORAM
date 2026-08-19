using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Extensions;

namespace ScoramAPI.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    [Authorize(Roles = "Student")]
    public class NotificationsController : ControllerBase
    {
        private readonly ScoramDbContext _db;

        public NotificationsController(ScoramDbContext db)
        {
            _db = db;
        }

        // GET /api/notifications?page=&pageSize= -- newest first, powers the bell dropdown list.
        [HttpGet]
        public async Task<ActionResult<List<NotificationResponseDto>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var userId = User.GetUserId();

            var notifications = await _db.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Skip((Math.Max(1, page) - 1) * pageSize)
                .Take(Math.Clamp(pageSize, 1, 50))
                .Select(n => new NotificationResponseDto
                {
                    Id = n.Id,
                    Type = n.Type.ToString(),
                    Title = n.Title,
                    Body = n.Body,
                    LinkUrl = n.LinkUrl,
                    IsRead = n.IsRead,
                    CreatedAt = n.CreatedAt
                })
                .ToListAsync();

            return Ok(notifications);
        }

        // GET /api/notifications/unread-count -- cheap poll-friendly endpoint for the bell badge.
        [HttpGet("unread-count")]
        public async Task<ActionResult<UnreadCountDto>> UnreadCount()
        {
            var userId = User.GetUserId();
            var count = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
            return Ok(new UnreadCountDto { Count = count });
        }

        [HttpPost("{id:guid}/read")]
        public async Task<IActionResult> MarkRead(Guid id)
        {
            var userId = User.GetUserId();
            var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
            if (notification == null) return NotFound();

            notification.IsRead = true;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("read-all")]
        public async Task<IActionResult> MarkAllRead()
        {
            var userId = User.GetUserId();
            var unread = await _db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync();
            foreach (var n in unread) n.IsRead = true;
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
