using ScoramAPI.Enums;

namespace ScoramAPI.Models
{
    // Durable notification-center history -- separate from the ephemeral SignalR "ReceiveMention"/
    // "ReceiveDirectMessage" events (those exist purely to update an already-open UI live; this table
    // is what powers the bell icon's list, survives a refresh, and is what a push notification is sent
    // from). One row per notification per recipient.
    public class Notification
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public NotificationType Type { get; set; }

        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;

        // Relative frontend path the bell item should navigate to on click -- computed once at
        // write-time (e.g. "/chat" or "/chat?tab=messages") so the client stays dumb about routing.
        public string LinkUrl { get; set; } = "/";

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // A browser's Web Push subscription (PushManager.subscribe() result) -- one row per
    // browser/device the student has granted notification permission on. A user can have several
    // (phone + laptop), so this is keyed by Endpoint, not UserId, to avoid clobbering one with another.
    public class PushSubscription
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public string Endpoint { get; set; } = string.Empty;
        public string P256dh { get; set; } = string.Empty;
        public string Auth { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // MOBILE PUSH (Firebase Cloud Messaging) -- the mobile app's equivalent of PushSubscription
    // above, which only ever covered browsers (Web Push/VAPID, ScoramWeb only). A device re-installing
    // the app or clearing data gets a new FCM token, so Token (not UserId+Platform) is the natural
    // unique key -- same "re-point an existing row to whichever account is now logged in on this
    // device" reasoning PushController.Subscribe already uses for Endpoint.
    public class DeviceToken
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public string Token { get; set; } = string.Empty;

        // "Android" | "iOS" -- purely informational (which platform this token is for); FCM's own
        // send API doesn't need it, it's just useful for admin visibility/debugging.
        public string Platform { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
