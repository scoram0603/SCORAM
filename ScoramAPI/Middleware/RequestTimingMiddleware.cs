using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Routing;

namespace ScoramAPI.Middleware
{
    // Non-functional diagnostics only. For every HTTP request this:
    //   1. adds an `X-Trace-Id` response header (the same id that ExceptionHandlingMiddleware already puts
    //      in the JSON body of a 500, and that Serilog's request log carries), so a client or a support
    //      ticket can quote one id and the matching server log line can be found;
    //   2. adds a standard `Server-Timing: app;dur=<ms>` header with the server-side duration, so slow
    //      responses can be attributed to "server" vs "network / Render cold start" from the client side;
    //   3. logs ONE warning for any request slower than Observability:SlowRequestMs (default 1500 ms),
    //      naming the route TEMPLATE (e.g. "api/tests/attempts/{attemptId:guid}") rather than the concrete
    //      URL, so ids never end up in the slow-request log and requests aggregate by endpoint.
    //
    // It never reads or logs request/response bodies, headers other than the ones it adds, the query
    // string, the Authorization header or the user's identity. It does not change status codes, bodies or
    // any existing header, and it does nothing that can throw into the pipeline.
    //
    // SignalR/WebSocket requests are deliberately skipped for the slow-request warning: a chat connection
    // is supposed to stay open for hours and would otherwise log a "slow request" on every disconnect.
    public class RequestTimingMiddleware
    {
        public const string TraceHeader = "X-Trace-Id";
        public const string TimingHeader = "Server-Timing";
        public const string ConfigKey = "Observability:SlowRequestMs";
        public const int DefaultSlowRequestMs = 1500;

        private readonly RequestDelegate _next;
        private readonly ILogger<RequestTimingMiddleware> _logger;
        private readonly int _slowRequestMs;

        public RequestTimingMiddleware(RequestDelegate next, ILogger<RequestTimingMiddleware> logger, IConfiguration configuration)
        {
            _next = next;
            _logger = logger;
            var configured = configuration.GetValue<int?>(ConfigKey);
            // 0 or a negative value would log every request; treat anything non-positive as "use default".
            _slowRequestMs = configured is > 0 ? configured.Value : DefaultSlowRequestMs;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            var traceId = context.TraceIdentifier;

            context.Response.OnStarting(() =>
            {
                // Set (not Add): never throws if some other component already wrote the header.
                context.Response.Headers[TraceHeader] = traceId;
                context.Response.Headers[TimingHeader] =
                    "app;dur=" + stopwatch.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);
                return Task.CompletedTask;
            });

            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();
                if (IsSlow(stopwatch.Elapsed, _slowRequestMs) && !IsLongLived(context))
                {
                    _logger.LogWarning(
                        "Slow request: {Method} {Route} -> {StatusCode} in {ElapsedMs} ms (trace {TraceId})",
                        context.Request.Method,
                        RouteLabel(context),
                        context.Response.StatusCode,
                        (long)stopwatch.Elapsed.TotalMilliseconds,
                        traceId);
                }
            }
        }

        internal static bool IsSlow(TimeSpan elapsed, int thresholdMs) => elapsed.TotalMilliseconds >= thresholdMs;

        private static bool IsLongLived(HttpContext context) =>
            context.WebSockets.IsWebSocketRequest
            || context.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase);

        // Route template when routing matched (no ids), otherwise just the path (e.g. a 404 probe).
        internal static string RouteLabel(HttpContext context)
        {
            if (context.GetEndpoint() is RouteEndpoint routeEndpoint && routeEndpoint.RoutePattern.RawText is { Length: > 0 } raw)
                return raw;
            return context.Request.Path.Value ?? "/";
        }
    }
}
