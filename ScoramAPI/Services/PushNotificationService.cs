using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using WebPush;

namespace ScoramAPI.Services
{
    public interface IPushNotificationService
    {
        /// <summary>Sends a push to every browser/device this user has subscribed on. Silently a
        /// no-op if they have none, if VAPID keys aren't configured, or if sending fails for any one
        /// subscription -- a missed push is never something that should break the caller's request.</summary>
        Task SendAsync(Guid userId, string title, string body, string linkUrl);
    }

    // Requires the "WebPush" NuGet package (`dotnet add package WebPush`) -- not available to install
    // in the sandbox this was written in, so this file hasn't been compiled. See appsettings.json for
    // the VapidKeys section this reads from.
    public class PushNotificationService : IPushNotificationService
    {
        private readonly ScoramDbContext _db;
        private readonly ILogger<PushNotificationService> _logger;
        private readonly VapidDetails? _vapidDetails;

        public PushNotificationService(ScoramDbContext db, IConfiguration config, ILogger<PushNotificationService> logger)
        {
            _db = db;
            _logger = logger;

            var publicKey = config["VapidKeys:PublicKey"];
            var privateKey = config["VapidKeys:PrivateKey"];
            var subject = config["VapidKeys:Subject"];

            _vapidDetails = string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey)
                ? null // Push is simply disabled until VapidKeys is configured -- not a startup failure.
                : new VapidDetails(string.IsNullOrWhiteSpace(subject) ? "mailto:admin@scoram.app" : subject, publicKey, privateKey);
        }

        public async Task SendAsync(Guid userId, string title, string body, string linkUrl)
        {
            if (_vapidDetails == null) return;

            var subscriptions = await _db.PushSubscriptions.Where(p => p.UserId == userId).ToListAsync();
            if (subscriptions.Count == 0) return;

            var payload = JsonSerializer.Serialize(new { title, body, url = linkUrl });
            var client = new WebPushClient();
            var staleSubscriptionIds = new List<Guid>();

            foreach (var sub in subscriptions)
            {
                try
                {
                    var pushSubscription = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                    await client.SendNotificationAsync(pushSubscription, payload, _vapidDetails);
                }
                catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
                {
                    // Browser revoked/expired this subscription (user cleared site data, uninstalled,
                    // etc.) -- stop trying to push to it instead of failing on every future notification.
                    staleSubscriptionIds.Add(sub.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Push notification failed for user {UserId}, endpoint {Endpoint}", userId, sub.Endpoint);
                }
            }

            if (staleSubscriptionIds.Count > 0)
            {
                _db.PushSubscriptions.RemoveRange(_db.PushSubscriptions.Where(p => staleSubscriptionIds.Contains(p.Id)));
                await _db.SaveChangesAsync();
            }
        }
    }
}
