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
    /// <summary>The structured content of one push. Navigation on the device is driven by
    /// Type / EntityType / EntityId (never by parsing the title/body text); LinkUrl is the legacy WEB
    /// path kept for the browser Web Push payload and as a mobile fallback.</summary>
    public sealed class PushPayload
    {
        public Guid NotificationId { get; init; }
        public string Type { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Body { get; init; } = string.Empty;
        public string LinkUrl { get; init; } = "/";
        public string? EntityType { get; init; }
        public string? EntityId { get; init; }
    }

    public interface IPushNotificationService
    {
        /// <summary>Sends a push to every browser (Web Push/VAPID) AND every ACTIVE mobile device
        /// (Firebase Cloud Messaging) this user has registered. A no-op per channel if the user has
        /// nothing registered there, if that channel isn't configured, or if sending fails -- a missed
        /// push must never break the caller. NotificationService.CreateAsync is the only caller, so
        /// every notification-producing feature already goes through here.</summary>
        Task SendAsync(Guid userId, PushPayload payload);
    }

    public class PushNotificationService : IPushNotificationService
    {
        /// <summary>Must match the channel created natively in MainActivity.kt and referenced by the
        /// AndroidManifest default_notification_channel_id meta-data.</summary>
        public const string AndroidChannelId = "scoram_notifications";

        /// <summary>Drawable resource name of the monochrome status-bar glyph (res/drawable/ic_stat_scoram.xml).</summary>
        public const string AndroidSmallIcon = "ic_stat_scoram";

        private const string AndroidAccentColor = "#0B2A52"; // SCORAM navy

        // FirebaseApp.Create throws if called twice per process; this class is scoped, so its constructor
        // can run many times -- serialize and re-check.
        private static readonly object FirebaseInitLock = new();
        private static bool _fcmInitAttempted;

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
                ? null // Web push is simply disabled until VapidKeys is configured -- not a startup failure.
                : new VapidDetails(string.IsNullOrWhiteSpace(subject) ? "mailto:admin@scoram.app" : subject, publicKey, privateKey);

            EnsureFirebase(config, logger);
        }

        private static bool FcmConfigured => FirebaseApp.DefaultInstance != null;

        // Credentials come from SERVER-SIDE configuration only -- never the Flutter app or React.
        // Two supported shapes (same "disabled until configured, not a startup failure" pattern as Msg91/Vapid):
        //   1. Firebase:ProjectId + Firebase:ClientEmail + Firebase:PrivateKey  (env vars
        //      Firebase__ProjectId, Firebase__ClientEmail, Firebase__PrivateKey -- the natural fit for
        //      Azure App Service / containers, where there is no file to point at)
        //   2. Firebase:ServiceAccountKeyPath -> a service-account JSON file kept OUTSIDE the repo
        private static void EnsureFirebase(IConfiguration config, ILogger logger)
        {
            if (_fcmInitAttempted) return;
            lock (FirebaseInitLock)
            {
                if (_fcmInitAttempted) return;
                _fcmInitAttempted = true;
                if (FirebaseApp.DefaultInstance != null) return;

                try
                {
                    GoogleCredential? credential = null;

                    var projectId = config["Firebase:ProjectId"];
                    var clientEmail = config["Firebase:ClientEmail"];
                    var privateKey = config["Firebase:PrivateKey"];
                    if (!string.IsNullOrWhiteSpace(projectId) && !string.IsNullOrWhiteSpace(clientEmail) && !string.IsNullOrWhiteSpace(privateKey))
                    {
                        // Env vars flatten the key's newlines to a literal "\n" -- restore real ones.
                        var json = JsonSerializer.Serialize(new Dictionary<string, string>
                        {
                            ["type"] = "service_account",
                            ["project_id"] = projectId,
                            ["client_email"] = clientEmail,
                            ["private_key"] = privateKey.Replace("\\n", "\n"),
                            ["token_uri"] = "https://oauth2.googleapis.com/token"
                        });
                        credential = GoogleCredential.FromJson(json);
                    }
                    else
                    {
                        var path = config["Firebase:ServiceAccountKeyPath"];
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                            credential = GoogleCredential.FromFile(path);
                    }

                    if (credential == null) return; // not configured -> mobile push stays off

                    FirebaseApp.Create(new AppOptions { Credential = credential, ProjectId = projectId });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to initialize Firebase -- mobile push notifications will be disabled.");
                }
            }
        }

        public async Task SendAsync(Guid userId, PushPayload payload)
        {
            await SendWebPushAsync(userId, payload);
            await SendFcmAsync(userId, payload);
        }

        private async Task SendWebPushAsync(Guid userId, PushPayload payload)
        {
            if (_vapidDetails == null) return;

            var subscriptions = await _db.PushSubscriptions.Where(p => p.UserId == userId).ToListAsync();
            if (subscriptions.Count == 0) return;

            var json = JsonSerializer.Serialize(new
            {
                title = payload.Title,
                body = payload.Body,
                url = payload.LinkUrl,
                type = payload.Type,
                notificationId = payload.NotificationId
            });
            var client = new WebPushClient();
            var staleSubscriptionIds = new List<Guid>();

            foreach (var sub in subscriptions)
            {
                try
                {
                    var pushSubscription = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                    await client.SendNotificationAsync(pushSubscription, json, _vapidDetails);
                }
                catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
                {
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

        // MOBILE PUSH -- one consistent payload strategy ("notification + data"):
        //   * `notification` (title/body) makes Android's SYSTEM draw the tray entry whenever the app is
        //     in the BACKGROUND or TERMINATED -- no Dart code runs, so no duplicate is possible there.
        //   * `data` carries the structured navigation fields the Flutter tap handler routes on.
        //   * While the app is in the FOREGROUND Android does not draw `notification` messages itself;
        //     Flutter's onMessage handler shows exactly one local notification instead.
        // Every device registered AND ACTIVE for the user gets its own copy (multi-device); a dead token
        // is deactivated individually and never affects the user's other devices.
        private async Task SendFcmAsync(Guid userId, PushPayload payload)
        {
            if (!FcmConfigured)
            {
                // Most common reason "push never arrives when the app is closed": no server credentials.
                // Set Firebase__ProjectId / Firebase__ClientEmail / Firebase__PrivateKey (or
                // Firebase__ServiceAccountKeyPath) for project scoram-b483e and restart the API.
                _logger.LogWarning("FCM is NOT configured on this server -- mobile push skipped for user {UserId}. Set Firebase__* credentials.", userId);
                return;
            }

            var tokens = await _db.DeviceTokens
                .Where(d => d.UserId == userId && d.IsActive)
                .Select(d => d.Token)
                .ToListAsync();
            if (tokens.Count == 0)
            {
                _logger.LogInformation("FCM: user {UserId} has no ACTIVE device tokens (never registered, logged out, or token deactivated).", userId);
                return;
            }

            var message = new MulticastMessage
            {
                Tokens = tokens,
                Notification = new FirebaseAdmin.Messaging.Notification { Title = payload.Title, Body = payload.Body },
                // FCM data values must be non-null strings. recipientId lets the app drop a push that
                // reaches a device now signed in as a DIFFERENT user (account-switch safety net).
                Data = new Dictionary<string, string>
                {
                    ["notificationId"] = payload.NotificationId.ToString(),
                    ["type"] = payload.Type,
                    ["entityType"] = payload.EntityType ?? string.Empty,
                    ["entityId"] = payload.EntityId ?? string.Empty,
                    ["linkUrl"] = payload.LinkUrl ?? string.Empty,
                    ["recipientId"] = userId.ToString()
                },
                Android = new AndroidConfig
                {
                    Priority = Priority.High,
                    Notification = new AndroidNotification
                    {
                        ChannelId = AndroidChannelId,
                        Icon = AndroidSmallIcon,
                        Color = AndroidAccentColor
                    }
                },
                Apns = new ApnsConfig { Aps = new Aps { Sound = "default" } }
            };

            try
            {
                var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
                _logger.LogInformation("FCM: user {UserId} -> {Success} sent, {Failure} failed ({Total} device(s)).",
                    userId, response.SuccessCount, response.FailureCount, tokens.Count);
                var deadTokens = new List<string>();
                for (var i = 0; i < response.Responses.Count; i++)
                {
                    var r = response.Responses[i];
                    if (r.IsSuccess) continue;

                    var code = r.Exception?.MessagingErrorCode;
                    // UNREGISTERED = app uninstalled / token expired. SENDER_ID_MISMATCH = token belongs to
                    // a different Firebase project. Both are permanent for THIS token.
                    // INVALID_ARGUMENT is ambiguous (bad token OR bad payload), so only treat it as a dead
                    // token when FCM's message says so -- otherwise one malformed payload would deactivate
                    // every device a user has.
                    var deadByCode = code is MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch;
                    var deadByMessage = code == MessagingErrorCode.InvalidArgument
                        && (r.Exception?.Message?.Contains("registration token", StringComparison.OrdinalIgnoreCase) ?? false);
                    if (deadByCode || deadByMessage)
                        deadTokens.Add(tokens[i]);
                    else
                        _logger.LogWarning(r.Exception, "FCM send failed for user {UserId} ({Code})", userId, code);
                }

                if (deadTokens.Count > 0)
                {
                    var now = DateTime.UtcNow;
                    await _db.DeviceTokens
                        .Where(d => deadTokens.Contains(d.Token))
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(d => d.IsActive, false)
                            .SetProperty(d => d.UpdatedAt, now));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FCM push failed for user {UserId}", userId);
            }
        }
    }
}
