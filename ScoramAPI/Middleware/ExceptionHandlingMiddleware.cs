using System.Net;
using System.Text.Json;

namespace ScoramAPI.Middleware
{
    // Catches any unhandled exception from anywhere further down the pipeline (controllers, model
    // binding, EF Core, etc.) and turns it into a consistent JSON error shape instead of the default
    // ASP.NET Core behavior (a blank 500 in Production, or a raw stack-trace HTML page in Development).
    //
    // This is purely additive: every existing controller already returns its own error responses
    // (BadRequest/NotFound/Conflict/Forbid/...) via normal `return` statements, which never throw --
    // this middleware never sees those. It only fires for genuinely unhandled exceptions (null refs,
    // DB timeouts, etc.) that previously would have produced ASP.NET Core's default response, so no
    // existing intentional behavior changes.
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;
        private readonly IHostEnvironment _env;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment env)
        {
            _next = next;
            _logger = logger;
            _env = env;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

                if (context.Response.HasStarted)
                {
                    // Response already partially written (e.g. streaming) -- nothing safe to do but rethrow.
                    throw;
                }

                context.Response.Clear();
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "application/json";

                var payload = new
                {
                    message = "Something went wrong on our end. Please try again, and contact support if it keeps happening.",
                    // Only leak exception detail in Development -- never in Production, where it could
                    // expose internals (connection strings, file paths, stack traces) to end users.
                    detail = _env.IsDevelopment() ? ex.ToString() : null,
                    traceId = context.TraceIdentifier
                };

                await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
            }
        }
    }
}
