namespace ScoramAPI.DTOs
{
    public class NotificationResponseDto
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string LinkUrl { get; set; } = "/";
        public bool IsRead { get; set; }
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

    public class VapidPublicKeyDto
    {
        public string PublicKey { get; set; } = string.Empty;
    }
}
