using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Hubs;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    /// <summary>Everything needed to create one notification. EntityType/EntityId give the mobile app a
    /// structured navigation target; DedupKey makes creation idempotent per user.</summary>
    public sealed class NotificationRequest
    {
        public NotificationType Type { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Body { get; init; } = string.Empty;
        public string LinkUrl { get; init; } = "/";
        public string? EntityType { get; init; }
        public string? EntityId { get; init; }
        public string? DedupKey { get; init; }
    }

    public interface INotificationService
    {
        /// <summary>Original signature -- kept so every existing call site (DMs, group mentions,
        /// discussion mentions, quiz challenges) compiles and behaves exactly as before.</summary>
        Task<NotificationResponseDto?> CreateAsync(Guid userId, NotificationType type, string title, string body, string linkUrl);

        /// <summary>Creates a notification-center row, pushes it live to the bell via SignalR, and queues
        /// the Web Push + FCM delivery (off the request path). Respects the recipient's mute preferences.
        /// Returns null if muted, the user is inactive/unknown, or DedupKey was already used for this user.</summary>
        Task<NotificationResponseDto?> CreateAsync(Guid userId, NotificationRequest request);

        /// <summary>
        /// Same as <see cref="CreateAsync(Guid, NotificationRequest)"/> but runs on the background work
        /// queue, so a request that triggers a notification (sending a chat message) never makes the
        /// sender wait for the recipient's notification row + hub push + FCM hand-off.
        /// </summary>
        void Enqueue(Guid userId, NotificationRequest request);
    }

    public class NotificationService : INotificationService
    {
        private const int MaxTitleLength = 120;
        private const int MaxBodyLength = 240;

        private readonly ScoramDbContext _db;
        private readonly IHubContext<ChatHub> _hub;
        private readonly INotificationWorkQueue _queue;

        public NotificationService(ScoramDbContext db, IHubContext<ChatHub> hub, INotificationWorkQueue queue)
        {
            _db = db;
            _hub = hub;
            _queue = queue;
        }

        public Task<NotificationResponseDto?> CreateAsync(Guid userId, NotificationType type, string title, string body, string linkUrl) =>
            CreateAsync(userId, new NotificationRequest { Type = type, Title = title, Body = body, LinkUrl = linkUrl });

        public void Enqueue(Guid userId, NotificationRequest request) =>
            _queue.Enqueue((sp, _) => sp.GetRequiredService<INotificationService>().CreateAsync(userId, request));

        public async Task<NotificationResponseDto?> CreateAsync(Guid userId, NotificationRequest request)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null || !user.IsActive) return null;
            if (IsMuted(user, request.Type)) return null;

            if (request.DedupKey != null &&
                await _db.Notifications.AnyAsync(n => n.UserId == userId && n.DedupKey == request.DedupKey))
                return null;

            // DIRECT MESSAGES: one notification row PER CONVERSATION, not per message. A burst of
            // messages from the same person re-uses (and re-opens) that row with the newest preview
            // instead of piling up a new entry each time -- the unread COUNT shown next to it is
            // computed from the actual unread messages (see NotificationsController.List). The hub
            // event and push below still fire for every message, so delivery stays instant.
            if (request.Type == NotificationType.DirectMessage && request.EntityType != null && request.EntityId != null)
            {
                var existing = await _db.Notifications
                    .Where(n => n.UserId == userId && n.Type == NotificationType.DirectMessage
                                && n.EntityType == request.EntityType && n.EntityId == request.EntityId)
                    .OrderByDescending(n => n.CreatedAt)
                    .ToListAsync();

                if (existing.Count > 0)
                {
                    var keep = existing[0];
                    keep.Title = Clean(request.Title, MaxTitleLength);
                    keep.Body = Clean(request.Body, MaxBodyLength);
                    keep.LinkUrl = string.IsNullOrWhiteSpace(request.LinkUrl) ? "/" : request.LinkUrl;
                    keep.IsRead = false;
                    keep.ReadAt = null;
                    keep.CreatedAt = DateTime.UtcNow;
                    // Rows from before this change: collapse the stragglers into the one we keep.
                    if (existing.Count > 1) _db.Notifications.RemoveRange(existing.Skip(1));
                    await _db.SaveChangesAsync();
                    return await PublishAsync(userId, keep);
                }
            }

            var notification = new Notification
            {
                UserId = userId,
                Type = request.Type,
                Title = Clean(request.Title, MaxTitleLength),
                Body = Clean(request.Body, MaxBodyLength),
                LinkUrl = string.IsNullOrWhiteSpace(request.LinkUrl) ? "/" : request.LinkUrl,
                EntityType = request.EntityType,
                EntityId = request.EntityId,
                DedupKey = request.DedupKey
            };
            _db.Notifications.Add(notification);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException) when (request.DedupKey != null)
            {
                // Lost a race against another request creating the same (UserId, DedupKey) -- the
                // unique index did its job. Detach so this scoped context stays usable, and report "already sent".
                _db.Entry(notification).State = EntityState.Detached;
                return null;
            }

            return await PublishAsync(userId, notification);
        }

        // Shared by the create and the "re-use this conversation's row" paths above: builds the DTO, pushes it
        // over the hub, and hands the FCM push to the background queue.
        private async Task<NotificationResponseDto> PublishAsync(Guid userId, Notification notification)
        {
            var dto = new NotificationResponseDto
            {
                Id = notification.Id,
                Type = notification.Type.ToString(),
                Title = notification.Title,
                Body = notification.Body,
                LinkUrl = notification.LinkUrl,
                EntityType = notification.EntityType,
                EntityId = notification.EntityId,
                IsRead = notification.IsRead,
                CreatedAt = notification.CreatedAt
            };

            // Live bell update -- best-effort: a dropped realtime event just means the bell catches up
            // on next open/poll.
            try
            {
                await _hub.Clients.Group($"user-{userId}").SendAsync("ReceiveNotification", dto);
            }
            catch { /* see DirectMessagesController for why this is intentionally swallowed */ }

            // System-tray push: queued, never awaited here, so the originating request (a sent chat
            // message, a publish click) doesn't slow down on Firebase. The worker resolves its own scope.
            var payload = new PushPayload
            {
                NotificationId = notification.Id,
                Type = notification.Type.ToString(),
                Title = notification.Title,
                Body = notification.Body,
                LinkUrl = notification.LinkUrl,
                EntityType = notification.EntityType,
                EntityId = notification.EntityId
            };
            _queue.Enqueue((sp, _) => sp.GetRequiredService<IPushNotificationService>().SendAsync(userId, payload));

            return dto;
        }

        // Existing preference semantics preserved exactly: Mention -> NotifyOnGroupMessages, every
        // original type (DirectMessage, QuizChallenge) -> NotifyOnDirectMessages. DiscussionReply follows
        // the discussion-mention preference. Content/system types (new test/mock/PYP, announcements...)
        // have NO per-type toggle yet -- add columns here when the settings screen grows them.
        private static bool IsMuted(User user, NotificationType type) => type switch
        {
            NotificationType.Mention or NotificationType.DiscussionReply => !user.NotifyOnGroupMessages,
            NotificationType.DirectMessage or NotificationType.QuizChallenge => !user.NotifyOnDirectMessages,
            _ => false
        };

        // Notification text is rendered as plain text on every client, but it can carry user-authored
        // content (DM previews, comment text) -- strip control characters and bound the length.
        private static string Clean(string? text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(Math.Min(text.Length, max + 1));
            foreach (var ch in text)
            {
                if (char.IsControl(ch) && ch != '\n') continue;
                sb.Append(ch);
                if (sb.Length >= max) break;
            }
            var cleaned = sb.ToString().Trim();
            return text.Length > max ? cleaned.TrimEnd() + "…" : cleaned;
        }
    }
}
