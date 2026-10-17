namespace ScoramAPI.Extensions
{
    // Small helpers for the informational client details stored on a session (RefreshToken). These are
    // client-supplied, so they are length-capped and used for display only -- never for a security decision.
    public static class ClientInfo
    {
        public const int MaxUserAgentLength = 300;

        public static string? GetUserAgent(this HttpContext context)
        {
            var raw = context.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = new string(raw.Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (raw.Length == 0) return null;
            return raw.Length > MaxUserAgentLength ? raw.Substring(0, MaxUserAgentLength) : raw;
        }

        // "Chrome on Windows", "Android app" ... a deliberately small, dependency-free summary for the
        // admin Sessions screen. Unknown agents fall back to a generic label.
        public static string Describe(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent)) return "Unknown device";
            var ua = userAgent;

            string os =
                ua.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android" :
                ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) || ua.Contains("iOS", StringComparison.OrdinalIgnoreCase) ? "iOS" :
                ua.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows" :
                ua.Contains("Mac OS", StringComparison.OrdinalIgnoreCase) || ua.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) ? "macOS" :
                ua.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux" : "";

            // Order matters: Edge and Opera also say "Chrome"; Chrome also says "Safari".
            string browser =
                ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge" :
                ua.Contains("OPR/", StringComparison.OrdinalIgnoreCase) ? "Opera" :
                ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox" :
                ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome" :
                ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari" :
                ua.Contains("Dart/", StringComparison.OrdinalIgnoreCase) || ua.Contains("okhttp", StringComparison.OrdinalIgnoreCase) ? "SCORAM app" : "";

            if (browser.Length > 0 && os.Length > 0) return $"{browser} on {os}";
            if (browser.Length > 0) return browser;
            if (os.Length > 0) return os;
            return "Unknown device";
        }
    }
}
