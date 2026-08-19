using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Hubs;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    public interface INotificationService
    {
        /// <summary>Creates a notification-center row, pushes it live to the bell via SignalR, and
        /// fires a Web Push -- all three respect the recipient's mute preference (NotifyOnGroupMessages
        /// for Mention, NotifyOnDirectMessages for DirectMessage). Returns null if muted, so callers
        /// don't need their own preference-checking logic.</summary>
        Task<NotificationResponseDto?> CreateAsync(Guid userId, NotificationType type, string title, string body, string linkUrl);
    }

    public class NotificationService : INotificationService
    {
        private readonly ScoramDbContext _db;
        private readonly IHubContext<ChatHub> _hub;
        private readonly IPushNotificationService _push;

        public NotificationService(ScoramDbContext db, IHubContext<ChatHub> hub, IPushNotificationService push)
        {
            _db = db;
            _hub = hub;
            _push = push;
        }

        public async Task<NotificationResponseDto?> CreateAsync(Guid userId, NotificationType type, string title, string body, string linkUrl)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null) return null;

            var muted = type == NotificationType.Mention ? !user.NotifyOnGroupMessages : !user.NotifyOnDirectMessages;
            if (muted) return null;

            var notification = new Notification
            {
                UserId = userId,
                Type = type,
                Title = title,
                Body = body,
                LinkUrl = linkUrl
            };
            _db.Notifications.Add(notification);
            await _db.SaveChangesAsync();

            var dto = new NotificationResponseDto
            {
                Id = notification.Id,
                Type = notification.Type.ToString(),
                Title = notification.Title,
                Body = notification.Body,
                LinkUrl = notification.LinkUrl,
                IsRead = notification.IsRead,
                CreatedAt = notification.CreatedAt
            };

            // Live bell update -- reuses the same "user-{id}" group as everything else in chat.
            // Best-effort: a dropped realtime event just means the bell catches up on next open/poll.
            try
            {
                await _hub.Clients.Group($"user-{userId}").SendAsync("ReceiveNotification", dto);
            }
            catch { /* see DirectMessagesController for why this is intentionally swallowed */ }

            // Fire-and-forget-ish, but still awaited so any DB cleanup of stale subscriptions inside
            // it completes -- SendAsync itself already never throws out to here.
            await _push.SendAsync(userId, title, body, linkUrl);

            return dto;
        }
    }
}
