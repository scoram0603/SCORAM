using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging;

namespace ScoramAPI.Middleware
{
    /// <summary>
    /// SEC-004 -- configuration for how the effective client IP is derived. Bound from the "ClientIp"
    /// section (env vars: ClientIp__TrustedProxies__0, ClientIp__TrustedNetworks__0, ...).
    ///
    /// Two modes, chosen by what is configured:
    ///  * TRUSTED mode  -- TrustedProxies and/or TrustedNetworks set: ASP.NET Core's own
    ///    ForwardedHeadersMiddleware is used, and X-Forwarded-For / X-Forwarded-Proto are honoured ONLY when
    ///    the directly connected peer is one of those proxies (walked right-to-left, up to ForwardLimit).
    ///    This is the target configuration; it needs the real addresses of the hosting platform's proxy tier,
    ///    which this repository does not contain (see the SEC-004 report).
    ///  * INTERIM mode (nothing configured) -- see <see cref="ClientIpResolver.TryResolveInterim"/>.
    /// </summary>
    public sealed class ClientIpOptions
    {
        public const string SectionName = "ClientIp";

        /// <summary>Exact proxy addresses allowed to supply forwarded headers.</summary>
        public string[] TrustedProxies { get; set; } = Array.Empty<string>();

        /// <summary>Proxy address ranges (CIDR, e.g. "10.0.0.0/8") allowed to supply forwarded headers.</summary>
        public string[] TrustedNetworks { get; set; } = Array.Empty<string>();

        /// <summary>TRUSTED mode: max forwarded entries processed. Null = walk through every hop that is itself
        /// a trusted proxy and stop at the first one that is not (the standard multi-proxy setup).</summary>
        public int? ForwardLimit { get; set; }

        /// <summary>INTERIM mode: number of entries, counted from the right of X-Forwarded-For, that belong to the
        /// hosting platform's own proxy tier (0 = the rightmost entry is the client, the behaviour this code had
        /// before SEC-004). Set it only from observed evidence (see DiagnosticSampleLimit).</summary>
        public int SkipRightmostEntries { get; set; }

        /// <summary>When &gt; 0, the first N requests log the direct peer, the (sanitised) X-Forwarded-For /
        /// X-Forwarded-Proto values and the resulting client IP, so the real proxy chain can be read off a
        /// staging deployment. 0 (default) = off. Nothing else about the request is logged.</summary>
        public int DiagnosticSampleLimit { get; set; }

        public bool HasTrustedProxies => TrustedProxies.Length > 0 || TrustedNetworks.Length > 0;

        /// <summary>Validates the configuration and builds the ASP.NET Core options for TRUSTED mode. Throws
        /// on any malformed entry: a typo must not silently shrink or (worse) widen the trust boundary.</summary>
        public ForwardedHeadersOptions ToForwardedHeadersOptions()
        {
            if (ForwardLimit is < 1)
                throw new InvalidOperationException("ClientIp:ForwardLimit must be at least 1 (or unset).");

            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                ForwardLimit = ForwardLimit,
                RequireHeaderSymmetry = false
            };
            // The defaults trust loopback; only what is explicitly configured is trusted here.
            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();

            foreach (var raw in TrustedProxies)
            {
                if (!IPAddress.TryParse(raw?.Trim(), out var ip) || IPAddress.Any.Equals(ip) || IPAddress.IPv6Any.Equals(ip))
                    throw new InvalidOperationException($"ClientIp:TrustedProxies contains an invalid address: '{raw}'.");
                options.KnownProxies.Add(ip);
            }

            foreach (var raw in TrustedNetworks)
            {
                var parts = (raw ?? string.Empty).Trim().Split('/');
                if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var prefix) || !int.TryParse(parts[1], out var bits))
                    throw new InvalidOperationException($"ClientIp:TrustedNetworks contains an invalid CIDR: '{raw}'.");
                var max = prefix.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
                // /0 would trust every caller -- exactly the hole this setting exists to close.
                if (bits < 1 || bits > max)
                    throw new InvalidOperationException($"ClientIp:TrustedNetworks prefix length must be 1..{max}: '{raw}'.");
                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, bits));
            }

            if (SkipRightmostEntries < 0)
                throw new InvalidOperationException("ClientIp:SkipRightmostEntries cannot be negative.");
            return options;
        }
    }

    /// <summary>Pure, HTTP-free client-IP helpers (unit-tested directly).</summary>
    public static class ClientIpResolver
    {
        /// <summary>Longest X-Forwarded-For value that is parsed at all; anything longer is treated as malformed.</summary>
        public const int MaxHeaderLength = 2048;

        /// <summary>The key every IP-partitioned rate-limit policy uses: the EFFECTIVE client IP (whatever the
        /// forwarded-header handling established), with IPv4-in-IPv6 written as plain IPv4 so one client can't
        /// land in two buckets depending on how the address was spelled.</summary>
        public static string PartitionKey(HttpContext context)
        {
            var ip = context.Connection.RemoteIpAddress;
            return ip == null ? "unknown" : Normalize(ip).ToString();
        }

        public static IPAddress Normalize(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

        /// <summary>True for addresses that cannot belong to a remote public-internet caller: loopback, RFC 1918,
        /// link-local, 100.64/10 shared space, IPv6 loopback / unique-local / link-local.</summary>
        public static bool IsPrivateOrLoopback(IPAddress address)
        {
            var ip = Normalize(address);
            if (IPAddress.IsLoopback(ip)) return true;
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = ip.GetAddressBytes();
                return b[0] == 10
                    || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                    || (b[0] == 192 && b[1] == 168)
                    || (b[0] == 169 && b[1] == 254)
                    || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
            }
            return ip.AddressFamily == AddressFamily.InterNetworkV6 && (ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal);
        }

        /// <summary>Strict parse of one X-Forwarded-For entry: a bare IPv4 in canonical dotted form (optionally
        /// ":port"), or an IPv6 address (optionally "[...]" with ":port"). Anything else -- "unknown", "1", "010.1.1.1",
        /// names, scoped addresses, 0.0.0.0, empty -- is rejected rather than guessed at.</summary>
        public static bool TryParseEntry(string? token, out IPAddress address)
        {
            address = IPAddress.None;
            if (string.IsNullOrWhiteSpace(token)) return false;
            var t = token.Trim();
            if (t.Length > 64 || t.Contains('%')) return false;

            string ipText;
            if (t[0] == '[')
            {
                var close = t.IndexOf(']');
                if (close < 0) return false;
                var rest = t[(close + 1)..];
                if (rest.Length != 0 && !(rest[0] == ':' && rest.Length > 1 && rest[1..].All(char.IsAsciiDigit))) return false;
                ipText = t[1..close];
                if (!ipText.Contains(':')) return false;
            }
            else if (t.Count(c => c == ':') == 1)
            {
                var split = t.Split(':');
                if (split[1].Length == 0 || !split[1].All(char.IsAsciiDigit)) return false;
                ipText = split[0];
            }
            else
            {
                ipText = t;
            }

            if (!IPAddress.TryParse(ipText, out var parsed)) return false;
            if (parsed.AddressFamily == AddressFamily.InterNetwork)
            {
                if (ipText.Count(c => c == '.') != 3 || parsed.ToString() != ipText) return false;
            }
            else if (parsed.AddressFamily != AddressFamily.InterNetworkV6 || !ipText.Contains(':'))
            {
                return false;
            }
            if (IPAddress.Any.Equals(parsed) || IPAddress.IPv6Any.Equals(parsed)) return false;
            address = Normalize(parsed);
            return true;
        }

        /// <summary>Splits every X-Forwarded-For header line into trimmed, non-empty entries. Returns null when
        /// the value is too long to be trusted as well-formed.</summary>
        public static List<string>? SplitEntries(IEnumerable<string?> headerValues)
        {
            var entries = new List<string>();
            var total = 0;
            foreach (var value in headerValues)
            {
                if (string.IsNullOrEmpty(value)) continue;
                total += value.Length;
                if (total > MaxHeaderLength) return null;
                foreach (var part in value.Split(','))
                {
                    var s = part.Trim();
                    if (s.Length > 0) entries.Add(s);
                }
            }
            return entries;
        }

        /// <summary>
        /// INTERIM rule (no trusted proxies configured). Forwarded values are considered ONLY when the directly
        /// connected peer is a private/loopback address -- i.e. something inside the hosting platform's network, never
        /// a public-internet caller talking to the container directly. Then the client is the entry
        /// <paramref name="skipRightmost"/> places from the right; a chain shorter than that, or an entry that is not a
        /// well-formed IP, means "do not trust" and the peer address is kept. Never falls back to the leftmost or to a
        /// different entry. With skipRightmost = 0 this is the rightmost-entry behaviour the API had before SEC-004,
        /// minus the ability of a public caller to use it.
        /// </summary>
        public static bool TryResolveInterim(IPAddress? peer, IEnumerable<string?> forwardedFor, int skipRightmost, out IPAddress client)
        {
            client = peer ?? IPAddress.None;
            if (peer == null || !IsPrivateOrLoopback(peer) || skipRightmost < 0) return false;
            var entries = SplitEntries(forwardedFor);
            if (entries == null || entries.Count <= skipRightmost) return false;
            if (!TryParseEntry(entries[entries.Count - 1 - skipRightmost], out var parsed)) return false;
            client = parsed;
            return true;
        }

        /// <summary>Rightmost X-Forwarded-Proto token, only if it is exactly http or https.</summary>
        public static string? TryResolveProto(IEnumerable<string?> forwardedProto)
        {
            var entries = SplitEntries(forwardedProto);
            var last = entries is { Count: > 0 } ? entries[^1] : null;
            if (string.Equals(last, "https", StringComparison.OrdinalIgnoreCase)) return "https";
            if (string.Equals(last, "http", StringComparison.OrdinalIgnoreCase)) return "http";
            return null;
        }

        /// <summary>Keeps only characters that can legitimately appear in an IP list, so a hostile header value cannot
        /// inject log lines. Truncated.</summary>
        public static string SanitizeForLog(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sb = new StringBuilder(Math.Min(value.Length, 200));
            foreach (var c in value)
            {
                if (sb.Length >= 200) { sb.Append('~'); break; }
                sb.Append(char.IsAsciiHexDigit(c) || c is '.' or ':' or ',' or ' ' or '[' or ']' or 'x' ? c : '?');
            }
            return sb.ToString();
        }
    }

    /// <summary>INTERIM-mode middleware (see <see cref="ClientIpResolver.TryResolveInterim"/>).</summary>
    public sealed class InterimClientIpMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ClientIpOptions _options;
        private readonly ILogger<InterimClientIpMiddleware> _logger;
        private int _warnedPublicPeer;

        public InterimClientIpMiddleware(RequestDelegate next, ClientIpOptions options, ILogger<InterimClientIpMiddleware> logger)
        {
            _next = next;
            _options = options;
            _logger = logger;
        }

        public Task InvokeAsync(HttpContext context)
        {
            var connection = context.Connection;
            var peer = connection.RemoteIpAddress; // the directly connected hop, before anything is rewritten
            if (ClientIpResolver.TryResolveInterim(peer, context.Request.Headers["X-Forwarded-For"], _options.SkipRightmostEntries, out var client))
            {
                connection.RemoteIpAddress = client;
            }
            else if (peer != null && !ClientIpResolver.IsPrivateOrLoopback(peer)
                     && context.Request.Headers.ContainsKey("X-Forwarded-For")
                     && Interlocked.Exchange(ref _warnedPublicPeer, 1) == 0)
            {
                // If the platform's proxy reaches this container from a PUBLIC address, every user would now share that
                // address's rate-limit bucket. Say so once (no header content logged) so it is noticed on staging.
                _logger.LogWarning("SEC-004: a request from a public peer address carried X-Forwarded-For; it was ignored (interim mode trusts it only from private-network peers). If this is the hosting platform's own proxy, configure ClientIp:TrustedProxies/TrustedNetworks, otherwise all users behind it share one rate-limit bucket.");
            }

            // Same trust gate for the scheme: an unverified caller cannot declare the request https/http.
            if (peer != null && ClientIpResolver.IsPrivateOrLoopback(peer) && context.Request.Headers.ContainsKey("X-Forwarded-Proto"))
            {
                var proto = ClientIpResolver.TryResolveProto(context.Request.Headers["X-Forwarded-Proto"]);
                if (proto != null) context.Request.Scheme = proto;
            }
            return _next(context);
        }
    }

    /// <summary>Opt-in (ClientIp:DiagnosticSampleLimit) capture of the real proxy chain. Wraps the resolution
    /// middleware so it sees the raw peer/headers first and the effective client IP last.</summary>
    public sealed class ClientIpDiagnosticsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ClientIpDiagnosticsMiddleware> _logger;
        private readonly int _limit;
        private int _logged;

        public ClientIpDiagnosticsMiddleware(RequestDelegate next, ILogger<ClientIpDiagnosticsMiddleware> logger, ClientIpOptions options)
        {
            _next = next;
            _logger = logger;
            _limit = options.DiagnosticSampleLimit;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var sample = _limit > 0 && Volatile.Read(ref _logged) < _limit ? Interlocked.Increment(ref _logged) : int.MaxValue;
            if (sample > _limit)
            {
                await _next(context);
                return;
            }

            var peer = context.Connection.RemoteIpAddress?.ToString() ?? "none";
            var xff = ClientIpResolver.SanitizeForLog(context.Request.Headers["X-Forwarded-For"].ToString());
            var xfp = ClientIpResolver.SanitizeForLog(context.Request.Headers["X-Forwarded-Proto"].ToString());
            try
            {
                await _next(context);
            }
            finally
            {
                _logger.LogWarning("SEC-004 client-ip sample {Sample}/{Limit}: peer={Peer} xff=[{Xff}] xfp=[{Xfp}] effective={Effective}",
                    sample, _limit, peer, xff, xfp, context.Connection.RemoteIpAddress?.ToString() ?? "none");
            }
        }
    }

    public static class ClientIpApplicationBuilderExtensions
    {
        /// <summary>Registers client-IP resolution. Must be the FIRST middleware: rate limiting, request logging
        /// and every controller read the result through HttpContext.Connection.RemoteIpAddress.</summary>
        public static IApplicationBuilder UseClientIpResolution(this IApplicationBuilder app, ClientIpOptions options)
        {
            app.UseMiddleware<ClientIpDiagnosticsMiddleware>(options);
            if (options.HasTrustedProxies)
                app.UseForwardedHeaders(options.ToForwardedHeadersOptions());
            else
                app.UseMiddleware<InterimClientIpMiddleware>(options);
            return app;
        }
    }
}
