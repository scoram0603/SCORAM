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

        // PUSH NOTIFICATION SYSTEM -- structured navigation target, so the mobile app never has to
        // guess a destination from LinkUrl (a WEB path) or from the notification text. EntityType is
        // a short stable name ("Chat", "ChatRoom", "Question", "QuestionBankQuestion", "MockTest",
        // "Paper", "PracticeTemplate", ...); EntityId is that entity's Guid as a string. Both are
        // nullable: older rows and generic notifications (announcements) have no entity.
        public string? EntityType { get; set; }
        public string? EntityId { get; set; }

        public DateTime? ReadAt { get; set; }

        // Idempotency key, unique per (UserId, DedupKey) where not null -- e.g. "NewMockTest:{testId}".
        // A retried/duplicated event (publish clicked twice, job re-run) then cannot notify the same
        // student twice about the same logical thing. Null = no dedup (chat messages, etc.).
        public string? DedupKey { get; set; }

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

        // nvarchar(450) + UNIQUE index since AddPushNotificationMetadata -- before that it was
        // nvarchar(max) with no unique index, so two concurrent registrations of the same token
        // (login + onTokenRefresh) could create duplicate rows.
        public string Token { get; set; } = string.Empty;

        // "Android" | "iOS" -- purely informational (which platform this token is for); FCM's own
        // send API doesn't need it, it's just useful for admin visibility/debugging.
        public string Platform { get; set; } = string.Empty;

        public string? AppVersion { get; set; }

        // False = FCM reported this token dead (uninstalled / expired). Kept as a soft flag instead of
        // a delete so a failure is visible for debugging; sending only ever targets IsActive rows, and
        // a later register-device call with the same token reactivates it.
        public bool IsActive { get; set; } = true;

        public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
