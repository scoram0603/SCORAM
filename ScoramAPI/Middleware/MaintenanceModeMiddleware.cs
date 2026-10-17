using ScoramAPI.Services;

namespace ScoramAPI.Middleware
{
    // ADMIN PLATFORM -- server-side enforcement of "maintenance mode" (AppConfigService.MaintenanceEnabled).
    //
    // While the flag is on, every /api/* request answers 503 { maintenance: true, message } EXCEPT:
    //   * /api/admin/*    -- the admin panel must stay usable so someone can turn maintenance back off
    //   * /api/app-config -- clients read the maintenance state from here to show their own screen
    //   * any request carrying a valid Admin/SuperAdmin token (the admin pages also call a few shared routes)
    //   * non-/api paths: /health, /health/ready, /swagger, /uploads, the SignalR hub negotiation is NOT
    //     exempt on purpose (/hubs/chat is a student feature).
    //
    // FAILS OPEN: if the config cannot be read (database down), the request is allowed through -- a
    // broken config read must never take the whole API offline on top of whatever else is wrong.
    // Must be registered AFTER UseAuthentication so context.User is populated.
    public class MaintenanceModeMiddleware
    {
        private readonly RequestDelegate _next;

        public MaintenanceModeMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path;
            var isApi = path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase);

            if (!isApi
                || path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase)
                || path.StartsWithSegments("/api/app-config", StringComparison.OrdinalIgnoreCase)
                || context.User.IsInRole("Admin")
                || context.User.IsInRole("SuperAdmin"))
            {
                await _next(context);
                return;
            }

            bool enabled;
            string message = string.Empty;
            try
            {
                var config = context.RequestServices.GetRequiredService<IAppConfigService>();
                var all = await config.GetAllAsync(context.RequestAborted);
                enabled = all.TryGetValue(AppConfigService.MaintenanceEnabled, out var flag)
                          && string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
                if (enabled && all.TryGetValue(AppConfigService.MaintenanceMessage, out var msg)) message = msg;
            }
            catch
            {
                await _next(context);
                return;
            }

            if (!enabled)
            {
                await _next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers["Retry-After"] = "300";
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { maintenance = true, message });
        }
    }
}
