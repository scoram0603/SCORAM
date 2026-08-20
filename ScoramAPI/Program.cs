using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ScoramAPI.Data;
using ScoramAPI.Hubs;
using ScoramAPI.Middleware;
using ScoramAPI.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------- Port binding ----------
// The Dockerfile sets ASPNETCORE_URLS=http://+:8080, which is the normal ASP.NET Core mechanism and
// takes precedence whenever it's present. Render (and some other host-based PaaS providers) instead
// inject a PORT environment variable and expect the app to bind to that -- this fallback only kicks
// in when PORT is set and ASPNETCORE_URLS is NOT, so local development (which sets neither) and the
// Docker image (which sets ASPNETCORE_URLS) are both unaffected.
var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(renderPort) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls($"http://+:{renderPort}");
}

// ---------- Structured logging ----------
// Reads the "Serilog" section in appsettings.json (and appsettings.Development.json, which can
// override with more verbose levels locally) -- console output during development, plus a rolling
// daily file under logs/ so production issues can be investigated after the fact.
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration));

// ---------- Database ----------
builder.Services.AddDbContext<ScoramDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ---------- Services ----------
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IAdminPermissionService, AdminPermissionService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPushNotificationService, PushNotificationService>();
builder.Services.AddScoped<IBulkImportService, BulkImportService>();
builder.Services.AddScoped<IQuestionBankImportService, QuestionBankImportService>(); // SCORAM_QUESTION_BANK
builder.Services.AddScoped<ITestAttemptService, TestAttemptService>(); // SCORAM_TESTS
builder.Services.AddScoped<IGamificationService, GamificationService>(); // GAMIFICATION
builder.Services.AddSingleton<IChatPresenceService, ChatPresenceService>(); // GROUP CHAT -- online user list
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IInstantSearchService, InstantSearchService>();
builder.Services.AddScoped<IFallbackSearchService, FallbackSearchService>();

// ---------- Azure Blob Storage ----------
// BlobServiceClient is thread-safe and expensive to construct, so it's a Singleton -- built once
// from the connection string here. This does NOT connect to Azure; parsing a connection string is
// purely local, so this is safe to do even while AzureBlobStorage:ConnectionString in appsettings.json
// is still the DEMO placeholder. Nothing actually reaches Azure until an upload/download/delete call
// happens inside AzureBlobService.
builder.Services.AddSingleton(_ => new BlobServiceClient(builder.Configuration["AzureBlobStorage:ConnectionString"]));
builder.Services.AddScoped<IAzureBlobService, AzureBlobService>();

// ---------- Rate limiting ----------
// Applied to the student and admin login endpoints (see [EnableRateLimiting("login")] on
// AuthController.Login / AdminAuthController.Login) and student registration (AuthController.Register)
// -- a basic but real defense against credential-stuffing/brute-force and mass fake-account creation
// at the scale this app is meant to run at.
//
// Partitioned per client IP via AddPolicy(...GetFixedWindowLimiter...) rather than AddFixedWindowLimiter
// -- the previous AddFixedWindowLimiter("login", ...) created ONE shared bucket for every client hitting
// the endpoint, so 5 legitimate students trying to log in within the same minute from different places
// could lock every other student out of logging in for the rest of that window. Partitioning by IP gives
// each caller their own bucket, which is what "Per-IP, 5 attempts/minute" was always meant to mean.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    options.AddPolicy("register", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0
        }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ---------- Controllers & Swagger ----------
// Enums as strings ("Easy", "Medium", "A", "Pending", ...) in both requests and responses --
// without this, System.Text.Json defaults to raw integers (0/1/2...), which is fragile for any
// frontend code (magic numbers) and unreadable in Swagger. This affects every enum-typed field
// across the API (DifficultyLevel, OptionLetter, AdminTaskStatus, AdminRole, etc.) -- safe/additive
// since most controllers were already manually calling .ToString() on outbound enums anyway.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Scoram API",
        Version = "v1",
        Description = "Competitive Exam Preparation Platform — Learn, Discuss, Score"
    });

    var jwtScheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Paste the JWT token here (no need to prefix with 'Bearer ')."
    };
    options.AddSecurityDefinition("Bearer", jwtScheme);
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        { jwtScheme, new List<string>() }
    });
});

// ---------- SignalR (real-time chat) ----------
builder.Services.AddSignalR();

// ---------- JWT Authentication ----------
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSection["Issuer"],
        ValidAudience = jwtSection["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!))
    };

    // Allow SignalR clients to send the JWT via query string (browsers can't set headers on websocket upgrade)
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// ---------- CORS (React/Vite frontend, local dev + deployed) ----------
// Local dev origins are always included in Development so `dotnet run` keeps working with no extra
// setup. The deployed frontend's origin(s) come from configuration -- Cors:AllowedOrigins -- which in
// Render's env-var style is Cors__AllowedOrigins__0, Cors__AllowedOrigins__1, etc. This intentionally
// never falls back to AllowAnyOrigin(): the app uses AllowCredentials() for cookie/token-bearing
// requests, and browsers reject AllowAnyOrigin() + AllowCredentials() together anyway. If no origins
// are configured in a non-Development environment, cross-origin requests are simply rejected (fails
// closed rather than open) until Cors:AllowedOrigins is set.
var configuredCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
var devCorsOrigins = new[] { "http://localhost:5173", "http://localhost:3000" };
var corsOrigins = builder.Environment.IsDevelopment()
    ? devCorsOrigins.Concat(configuredCorsOrigins).Distinct().ToArray()
    : configuredCorsOrigins;

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});

var app = builder.Build();

// ---------- Forwarded headers ----------
// Render (like most host-based PaaS providers) terminates TLS at its edge and proxies plain HTTP to
// this container. Without this, UseHttpsRedirection() below would see every request as HTTP, and --
// more importantly -- Connection.RemoteIpAddress would always be Render's internal proxy IP, silently
// turning the per-IP login/register rate limiting configured above into one shared bucket for every
// client again (the exact bug that partitioning by IP was meant to fix). This has to run before
// anything else reads the scheme or remote IP, so it's the very first middleware. Clearing
// KnownNetworks/KnownProxies is the standard approach for platforms whose proxy IP isn't fixed or
// knowable in advance -- it only affects which X-Forwarded-* headers are trusted, not what the app
// otherwise accepts.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Must be the very first middleware so it can catch exceptions thrown by anything after it.
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();

// Swagger stays available in every environment (including production on Render) -- it has no
// hard-coded server URL, so Swashbuckle infers it from the incoming request and "Try it out" works
// correctly whether that's http://localhost:5192 locally or the deployed Render URL.
app.UseSwagger();
app.UseSwaggerUI();

// Serve uploaded images (exam logos, question/option/explanation diagrams) at /uploads/{subfolder}/{file}
var uploadsRoot = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "uploads");
Directory.CreateDirectory(Path.Combine(uploadsRoot, "exam-logos"));
Directory.CreateDirectory(Path.Combine(uploadsRoot, "question-images"));
Directory.CreateDirectory(Path.Combine(uploadsRoot, "chat-attachments"));
app.UseStaticFiles();

app.UseCors("FrontendDev");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// Lightweight liveness endpoint for Render's health checks -- deliberately does NOT touch the
// database or Meilisearch, so a slow/unavailable dependency never gets the whole container marked
// unhealthy and cycled. It only reports "the ASP.NET Core process is up and serving requests".
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.Run();
