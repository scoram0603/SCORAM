namespace ScoramAPI.Services
{
    // Database-free rules for the sender-avatar push (see PushNotificationService). Kept separate so they
    // can be unit tested and so the policy lives in one obvious place.
    public static class PushAvatarRules
    {
        /// <summary>First app version that can draw a data-only avatar push itself. Overridable with the
        /// <c>Push:AvatarMinAppVersion</c> setting so the server can be deployed BEFORE the app is
        /// released without any device losing notifications.</summary>
        public const string DefaultMinAppVersion = "0.1.2";

        public const int MaxPhotoUrlLength = 500;

        /// <summary>Notification types that come from one identifiable person and may show their avatar.
        /// Everything else (admin notices, test/mock/PYP reminders, achievements...) keeps the SCORAM icon.</summary>
        public static bool TypeShowsSenderAvatar(string? type) => type == "DirectMessage";

        /// <summary>
        /// A device gets the data-only "draw it yourself" push only if it is an Android device whose
        /// registered app version is known and new enough. Unknown / older / iOS devices keep the
        /// existing "notification + data" message, which the system draws — so an old app can never end up
        /// with a push that it silently does not display.
        /// </summary>
        public static bool DeviceCanDrawAvatar(string? platform, string? appVersion, string minVersion)
        {
            if (!string.Equals(platform, "Android", StringComparison.OrdinalIgnoreCase)) return false;
            if (!TryParseVersion(appVersion, out var have)) return false;
            if (!TryParseVersion(minVersion, out var need)) need = new Version(0, 1, 2);
            return have >= need;
        }

        // "0.1.2" or "0.1.2+3" or "0.1.2-beta" -> 0.1.2
        public static bool TryParseVersion(string? raw, out Version version)
        {
            version = new Version(0, 0);
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var core = raw.Trim().Split('+', '-')[0];
            if (!Version.TryParse(core, out var parsed)) return false;
            version = parsed;
            return true;
        }

        /// <summary>
        /// The stored profile photo reference, only if it is safe to put in a push: an https URL or an
        /// app-relative "/uploads/..." path, bounded in length, no whitespace/control characters. Anything
        /// else becomes empty and the device falls back to the default avatar. The photo was already
        /// public to other students inside the app; no private storage URL or credential is created here.
        /// </summary>
        public static string SanitizePhotoUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            var u = url.Trim();
            if (u.Length > MaxPhotoUrlLength) return string.Empty;
            if (u.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return string.Empty;
            var ok = u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                     || (u.StartsWith("/uploads/", StringComparison.Ordinal) && !u.Contains(".."));
            return ok ? u : string.Empty;
        }
    }
}
