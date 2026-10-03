namespace ScoramAPI.DTOs
{
    public class NotificationResponseDto
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string LinkUrl { get; set; } = "/";

        // Structured navigation target for the mobile app (see Notification.EntityType/EntityId).
        public string? EntityType { get; set; }
        public string? EntityId { get; set; }

        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class UnreadCountDto
    {
        public int Count { get; set; }
    }

    // Mirrors the shape of PushSubscriptionJSON from the browser's PushManager.subscribe() --
    // .keys.p256dh and .keys.auth are what Web Push encryption needs per-recipient.
    public class PushSubscribeDto
    {
        public string Endpoint { get; set; } = string.Empty;
        public string P256dh { get; set; } = string.Empty;
        public string Auth { get; set; } = string.Empty;
    }

    public class PushUnsubscribeDto
    {
        public string Endpoint { get; set; } = string.Empty;
    }

    // MOBILE PUSH -- see Models.DeviceToken's own comment on why Token (not UserId+Platform) is the
    // key this registers/re-points by.
    public class RegisterDeviceDto
    {
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(450)]
        public string Token { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.MaxLength(20)]
        public string Platform { get; set; } = string.Empty; // "Android" | "iOS"

        [System.ComponentModel.DataAnnotations.MaxLength(30)]
        public string? AppVersion { get; set; }
    }

    public class UnregisterDeviceDto
    {
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(450)]
        public string Token { get; set; } = string.Empty;
    }

    // ADMIN BROADCAST -- see AdminNotificationsController.
    public class SendAnnouncementDto
    {
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(120)]
        public string Title { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(240)]
        public string Body { get; set; } = string.Empty;

        /// <summary>"AllStudents" | "Exams" | "Users".</summary>
        public string Target { get; set; } = "AllStudents";
        public List<Guid>? ExamIds { get; set; }
        public List<Guid>? UserIds { get; set; }

        /// <summary>Where a tap should land -- a WHITELISTED key, never a free-form route: "Home" |
        /// "Notifications" | "Quizzes" | "MockTests" | "Pyp" | "Tests" | "Progress".</summary>
        public string Destination { get; set; } = "Notifications";
    }

    public class VapidPublicKeyDto
    {
        public string PublicKey { get; set; } = string.Empty;
    }
}
