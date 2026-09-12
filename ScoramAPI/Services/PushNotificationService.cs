using System.Net;
using System.Text.Json;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using WebPush;

namespace ScoramAPI.Services
{
    public interface IPushNotificationService
    {
        /// <summary>Sends a push to every browser (Web Push/VAPID) AND every mobile device (Firebase
        /// Cloud Messaging) this user has registered. Silently a no-op per-channel if they have no
        /// subscriptions/tokens on that channel, if that channel isn't configured, or if sending
        /// fails -- a missed push is never something that should break the caller's request. This is
        /// the single place every notification-producing feature in the app (DMs, group chat,
        /// comment replies, etc. -- see NotificationService.CreateAsync, the only caller) already
        /// goes through, which is exactly why FCM support was added here instead of touching every
        /// call site.</summary>
        Task SendAsync(Guid userId, string title, string body, string linkUrl);
    }

    // Requires the "WebPush" AND "FirebaseAdmin" NuGet packages (`dotnet add package WebPush`,
    // `dotnet add package FirebaseAdmin`) -- neither available to install in the sandbox this was
    // written in, so this file hasn't been compiled. See appsettings.json for the VapidKeys and
    // Firebase config sections this reads from.
    public class PushNotificationService : IPushNotificationService
    {
        private readonly ScoramDbContext _db;
        private readonly ILogger<PushNotificationService> _logger;
        private readonly VapidDetails? _vapidDetails;
        private readonly bool _fcmConfigured;

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

            // MOBILE PUSH (Firebase) -- same "disabled until configured, not a startup failure"
            // pattern as VapidDetails above. FirebaseApp.Create must only ever run once per process
            // (it throws SECOND time it's called) -- the DefaultInstance null-check guards against
            // that, since this constructor can legitimately run more than once per process depending
            // on DI lifetime/hosting setup.
            var serviceAccountPath = config["Firebase:ServiceAccountKeyPath"];
            _fcmConfigured = !string.IsNullOrWhiteSpace(serviceAccountPath) && File.Exists(serviceAccountPath);
            if (_fcmConfigured && FirebaseApp.DefaultInstance == null)
            {
                try
                {
                    FirebaseApp.Create(new AppOptions
                    {
                        Credential = GoogleCredential.FromFile(serviceAccountPath)
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize Firebase -- mobile push notifications will be disabled.");
                    _fcmConfigured = false;
                }
            }
        }

        public async Task SendAsync(Guid userId, string title, string body, string linkUrl)
        {
            await SendWebPushAsync(userId, title, body, linkUrl);
            await SendFcmAsync(userId, title, body, linkUrl);
        }

        private async Task SendWebPushAsync(Guid userId, string title, string body, string linkUrl)
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
                    _logger.LogWarning(ex, "Web push failed for user {UserId}, endpoint {Endpoint}", userId, sub.Endpoint);
                }
            }

            if (staleSubscriptionIds.Count > 0)
            {
                _db.PushSubscriptions.RemoveRange(_db.PushSubscriptions.Where(p => staleSubscriptionIds.Contains(p.Id)));
                await _db.SaveChangesAsync();
            }
        }

        // MOBILE PUSH -- Data-only-plus-Notification payload: Notification gets the OS to show a
        // system tray banner even while the app is backgrounded/killed (which a data-only payload
        // alone does NOT reliably do on iOS, and only does on Android via a foreground service the
        // app would have to run); Data carries linkUrl so the Flutter side's tap handler can navigate
        // to the right screen, same URL NotificationService already hands the web frontend's
        // ReceiveNotification SignalR event and the Web Push payload above.
        private async Task SendFcmAsync(Guid userId, string title, string body, string linkUrl)
        {
            if (!_fcmConfigured) return;

            var tokens = await _db.DeviceTokens.Where(d => d.UserId == userId).Select(d => d.Token).ToListAsync();
            if (tokens.Count == 0) return;

            var message = new MulticastMessage
            {
                Tokens = tokens,
                Notification = new FirebaseAdmin.Messaging.Notification { Title = title, Body = body },
                Data = new Dictionary<string, string> { ["linkUrl"] = linkUrl ?? string.Empty },
                Android = new AndroidConfig { Priority = Priority.High },
                Apns = new ApnsConfig { Aps = new Aps { ContentAvailable = true } }
            };

            try
            {
                var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
                var staleTokens = new List<string>();
                for (var i = 0; i < response.Responses.Count; i++)
                {
                    var r = response.Responses[i];
                    if (!r.IsSuccess &&
                        r.Exception?.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
                    {
                        // App uninstalled, or the token otherwise went stale -- stop pushing to it.
                        staleTokens.Add(tokens[i]);
                    }
                }

                if (staleTokens.Count > 0)
                {
                    _db.DeviceTokens.RemoveRange(_db.DeviceTokens.Where(d => staleTokens.Contains(d.Token)));
                    await _db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FCM push failed for user {UserId}", userId);
            }
        }
    }
}
