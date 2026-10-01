using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ScoramAPI.Data;
using ScoramAPI.Hubs;
using ScoramAPI.Middleware;
using ScoramAPI.Services;
using Serilog;
using StackExchange.Redis;

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
builder.Services.AddSingleton<ITotpService, TotpService>(); // stateless, pure algorithm -- see TotpService's own comment
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<ISecurityStampService, SecurityStampService>();
builder.Services.AddScoped<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IAdminPermissionService, AdminPermissionService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPushNotificationService, PushNotificationService>();
builder.Services.AddScoped<IMsg91Service, Msg91Service>();
builder.Services.AddSingleton<ICaptchaService, CaptchaService>(); // lightweight math captcha for Register/Login -- see CaptchaService's own comment
builder.Services.AddScoped<IBulkImportService, BulkImportService>();
builder.Services.AddScoped<IBulkPaperImportService, BulkPaperImportService>();
builder.Services.AddScoped<IQuestionBankImportService, QuestionBankImportService>(); // SCORAM_QUESTION_BANK
builder.Services.AddScoped<IBulkUploadZipService, BulkUploadZipService>(); // rich PYP/PYQ bulk upload -- ZIP package extraction
builder.Services.AddHostedService<BulkImportStagingCleanupService>(); // sweeps abandoned bulk-import staged images
builder.Services.AddScoped<IQuestionBankMirrorService, QuestionBankMirrorService>(); // auto-mirrors new PYQ questions into the Question Bank
builder.Services.AddScoped<IBulkImportCommitService, BulkImportCommitService>(); // extracted from BulkImportController.Commit -- see that class's own comment
builder.Services.AddScoped<IBulkPaperImportCommitService, BulkPaperImportCommitService>(); // extracted from BulkPaperImportController.Commit
builder.Services.AddScoped<IQuestionBankImportCommitService, QuestionBankImportCommitService>(); // extracted from QuestionBankAdminController.Commit
builder.Services.AddScoped<ITestAttemptService, TestAttemptService>(); // SCORAM_TESTS
builder.Services.AddScoped<ISubjectManagementService, SubjectManagementService>(); // Subject Management (admin)
builder.Services.AddScoped<IBusinessIdService, BusinessIdService>(); // Business IDs (EXMSSC001, SUB001, ...) -- SuperAdmin change + one-time backfill
builder.Services.AddScoped<IGamificationService, GamificationService>(); // GAMIFICATION
// ---------- Redis (optional -- see ConnectionStrings:Redis) ----------
// Everything below degrades gracefully to today's single-instance-only behavior when this isn't
// configured, or when it's configured but unreachable at startup: SignalR keeps working exactly as
// before (just without a backplane, so a message sent by a client connected to one instance won't
// reach a client connected to a different instance), presence tracking stays in-memory-per-instance
// (see ChatPresenceService/DmPresenceService's own comments), and IDistributedCache falls back to an
// in-memory implementation. None of this requires Redis to run the app at all -- it's what makes
// running MORE THAN ONE instance of it actually correct.
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
IConnectionMultiplexer? redisMultiplexer = null;
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    try
    {
        redisMultiplexer = ConnectionMultiplexer.Connect(redisConnectionString);
    }
    catch (Exception ex)
    {
        // Deliberately not fatal: Redis being unreachable at the exact moment this process started
        // shouldn't take the whole app down with it, any more than the DB being briefly unreachable
        // does (see the /health endpoint's own comment further down). Falls through to the
        // single-instance-only registrations below.
        Console.Error.WriteLine($"Redis configured but unreachable at startup, falling back to single-instance-only behavior: {ex.Message}");
    }
}

if (redisMultiplexer != null)
{
    builder.Services.AddSingleton(redisMultiplexer);
    builder.Services.AddSingleton<IChatPresenceService, RedisChatPresenceService>(); // GROUP CHAT -- online user list, now correct across instances
    builder.Services.AddSingleton<IDmPresenceService, RedisDmPresenceService>(); // DIRECT MESSAGES -- same reasoning
    builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redisConnectionString);
    builder.Services.AddSingleton<IBackgroundJobQueue, RedisBackgroundJobQueue>(); // moves Meilisearch indexing off the Publish/Unpublish/Delete request path -- see PapersController
    builder.Services.AddHostedService<SearchIndexWorker>(); // consumes the queue above -- no reason to run it when there's nothing to consume
    builder.Services.AddHostedService<BulkImportCommitWorker>(); // consumes bulk-import commit jobs -- see BulkImportController.Commit
    builder.Services.AddHostedService<BulkPaperImportCommitWorker>(); // consumes bulk-paper-import commit jobs -- see BulkPaperImportController.Commit
    builder.Services.AddHostedService<QuestionBankImportCommitWorker>(); // consumes question-bank-import commit jobs -- see QuestionBankAdminController.Commit
}
else
{
    builder.Services.AddSingleton<IChatPresenceService, ChatPresenceService>(); // GROUP CHAT -- online user list
    builder.Services.AddSingleton<IDmPresenceService, DmPresenceService>(); // DIRECT MESSAGES -- "Active now" / "Last seen"
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddSingleton<IBackgroundJobQueue, NullBackgroundJobQueue>(); // IsAvailable=false -- callers fall back to running the work inline, exactly as before this existed
}
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IInstantSearchService, InstantSearchService>();
builder.Services.AddScoped<IFallbackSearchService, FallbackSearchService>();
builder.Services.AddSingleton<IStagedDataCache, StagedDataCache>();

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

    // Partitioned by user id, not IP -- these are all [Authorize]-gated actions, and IP-partitioning
    // would wrongly throttle every student behind the same NAT/office/college wifi as one shared
    // bucket, the exact bug the login/register partitioning above was written to avoid in the first
    // place (just the IP-vs-user version of it). GetRateLimitPartitionKey falls back to IP only for
    // an unauthenticated caller -- UseRateLimiter runs before UseAuthorization in this pipeline (see
    // below), so a request with no/invalid token still needs a safe partition key here before
    // [Authorize] gets the chance to reject it with 401.
    //
    // "content-post": chat/DM messages, discussion comments and replies, and sharing a question/
    // paper into a room or conversation -- the actions that create new content another
    // user/room/conversation actually sees, so unrestricted volume is a direct spam/flood vector
    // against other students, not just a cost concern the way login/register's brute-force risk is.
    options.AddPolicy("content-post", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetRateLimitPartitionKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    // Upvote/downvote/poll-vote -- cheap individually, but an unthrottled script could still
    // manipulate a solution's ranking or a poll's result by hammering this. Looser than
    // "content-post" since a burst of legitimate votes while scrolling a long discussion thread is
    // normal, unlike a burst of 30 chat messages.
    options.AddPolicy("vote", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetRateLimitPartitionKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    // Reporting a message/comment -- deliberately the strictest of the three. A genuine user reports
    // things rarely; a mass-report script is either harassment (trying to get another user's content
    // auto-hidden/reviewed en masse) or pure noise that wastes admin moderation time either way.
    options.AddPolicy("report", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetRateLimitPartitionKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0
        }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Shared by every user-partitioned policy above -- see "content-post"'s own comment for why this
// falls back to IP instead of throwing when the caller isn't authenticated (checking
// User.Identity?.IsAuthenticated directly here rather than reusing GetUserId()'s throw-on-missing
// behavior, which is the right contract for code that already knows it's dealing with an
// authenticated principal, but not for a partition-key selector that runs before that's guaranteed).
static string GetRateLimitPartitionKey(HttpContext httpContext)
{
    if (httpContext.User.Identity?.IsAuthenticated == true)
    {
        var sub = httpContext.User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
                  ?? httpContext.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (sub != null) return $"user:{sub}";
    }
    return $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}

// ---------- Controllers & Swagger ----------
// Enums as strings ("Easy", "Medium", "A", "Pending", ...) in both requests and responses --
// without this, System.Text.Json defaults to raw integers (0/1/2...), which is fragile for any
// frontend code (magic numbers) and unreadable in Swagger. This affects every enum-typed field
// across the API (DifficultyLevel, OptionLetter, AdminTaskStatus, AdminRole, etc.) -- safe/additive
// since most controllers were already manually calling .ToString() on outbound enums anyway.
builder.Services.AddControllers(options => options.Filters.Add<ScoramAPI.Middleware.MustChangePasswordFilter>())
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
var signalRBuilder = builder.Services.AddSignalR();
if (redisMultiplexer != null)
{
    // Without this, a message sent by a client connected to one instance never reaches a client
    // connected to a different one -- see this file's earlier Redis comment block. This is the one
    // piece that actually needs the connection string rather than just the already-connected
    // multiplexer, since the SignalR Redis package manages its own separate connection internally.
    signalRBuilder.AddStackExchangeRedis(redisConnectionString!);
}

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
        },

        // Closes the "token still cryptographically valid but should no longer work" gap -- password
        // change, logout-everywhere, and deactivation all regenerate SecurityStamp (see
        // SecurityStampService), and this is what actually makes that regeneration reject an
        // already-issued access token instead of just affecting future logins.
        OnTokenValidated = async context =>
        {
            var principal = context.Principal;
            // Same dual-claim-type check as ClaimsPrincipalExtensions.GetUserId() -- and for the same
            // reason: MapInboundClaims isn't explicitly disabled anywhere in this file, so depending on
            // the token handler in use, "sub" may already have been remapped to the long
            // ClaimTypes.NameIdentifier URI by the time this event fires. Checking only one would work
            // fine against one handler and fail every single authenticated request against the other --
            // exactly the kind of thing that's easy to get wrong without testing both.
            var subClaim = principal?.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
                           ?? principal?.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
            var stampClaim = principal?.FindFirst("stamp")?.Value;

            if (subClaim == null || stampClaim == null || !Guid.TryParse(subClaim, out var principalId))
            {
                context.Fail("Token is missing required claims.");
                return;
            }

            var isAdmin = principal!.IsInRole("Admin") || principal.IsInRole("SuperAdmin");
            var stampService = context.HttpContext.RequestServices.GetRequiredService<ScoramAPI.Services.ISecurityStampService>();

            if (!await stampService.IsValidAsync(principalId, isAdmin, stampClaim))
            {
                context.Fail("Token has been revoked.");
            }
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
var devCorsOrigins = new[] { "http://localhost:5173", "http://localhost:3000", "https://scoram.in/" };
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
// this container, and doesn't publish a fixed/documented proxy IP range, so ASP.NET Core's built-in
// ForwardedHeadersMiddleware can't be given a real KnownProxies/KnownNetworks allow-list. Clearing
// those lists used to mean "trust every hop in the header" -- which let a client set its own
// X-Forwarded-For and have it taken as the real IP, defeating the per-IP login/register rate limits
// above. As of .NET 8.0.17/9.0.6 Microsoft hardened the built-in middleware the other way instead
// (see https://learn.microsoft.com/en-us/dotnet/core/compatibility/aspnet-core/8.0/forwarded-headers-unknown-proxies):
// an empty trusted-proxy list now means forwarded headers are ignored entirely, which would collapse
// the rate limiter back into one shared bucket for every client -- the exact bug partitioning by IP
// was meant to fix in the first place. Either behavior is wrong here.
//
// Render appends its own edge-determined client IP to X-Forwarded-For rather than overwriting
// whatever a client already sent (confirmed by Render's own support replies -- see
// https://feedback.render.com/features/p/send-the-correct-xforwardedfor and
// https://community.render.com/t/accessing-client-ips-in-a-node-express-app/36282), so the
// RIGHTMOST entry is always the one Render itself added right before the request reached this
// container. A client can prepend as many fake entries as it wants, but it cannot control what
// Render's own edge appends after them. This middleware trusts only that one entry instead of
// walking the whole header, so it works without knowing Render's actual IP range and isn't affected
// by the .NET patch-version change above. Must run before anything else reads the scheme or remote
// IP, so it's the very first middleware.
app.Use(async (context, next) =>
{
    var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
    if (!string.IsNullOrEmpty(forwardedFor))
    {
        var lastHop = forwardedFor.Split(',').Select(s => s.Trim()).LastOrDefault(s => s.Length > 0);
        if (lastHop != null && System.Net.IPAddress.TryParse(lastHop, out var realIp))
        {
            context.Connection.RemoteIpAddress = realIp;
        }
    }

    var forwardedProto = context.Request.Headers["X-Forwarded-Proto"].ToString();
    if (!string.IsNullOrEmpty(forwardedProto))
    {
        var lastProto = forwardedProto.Split(',').Select(s => s.Trim()).LastOrDefault(s => s.Length > 0);
        if (string.Equals(lastProto, "https", StringComparison.OrdinalIgnoreCase) || string.Equals(lastProto, "http", StringComparison.OrdinalIgnoreCase))
        {
            context.Request.Scheme = lastProto!;
        }
    }

    await next();
});

// Must be the very first middleware so it can catch exceptions thrown by anything after it.
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();

// Swagger is only served in Development by default -- exposing full API surface + "Try it out"
// (which accepts a real JWT) to the public internet is a needless attack-surface increase in
// production. Set Swagger:EnableInProduction=true (env var Swagger__EnableInProduction) if you
// deliberately want it reachable in a non-Development environment; it still has no hard-coded
// server URL, so Swashbuckle keeps inferring it from the incoming request either way.
var swaggerEnabled = app.Environment.IsDevelopment()
    || builder.Configuration.GetValue<bool>("Swagger:EnableInProduction");
if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Uploaded images (exam logos, question/option/explanation diagrams, profile photos, chat/DM
// attachments) used to live on local disk at wwwroot/uploads/{subfolder}/{file} and were served here
// via app.UseStaticFiles(). Render's filesystem is ephemeral -- it resets on every redeploy/restart --
// so anything written there at runtime was silently lost, leaving broken image URLs in the database.
// These now live in Azure Blob Storage (see AzureBlobService) and are served by
// UploadedFilesController at the same "/uploads/{subfolder}/{file}" route, so no data migration or
// frontend change was needed. UseStaticFiles() is kept for any other genuinely static wwwroot assets.
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

// One-time-per-restart check: creates the first SuperAdmin from SUPERADMIN_EMAIL/SUPERADMIN_PASSWORD
// if none exists yet. No-ops immediately on every later restart. See SuperAdminBootstrapService.
//
// Wrapped in try/catch deliberately: this runs before app.Run(), so an unhandled exception here
// (most likely: migrations haven't been applied yet -- there's no automatic Database.Migrate() call
// anywhere, migrations are applied manually/via the deploy pipeline) would stop the process from ever
// reaching app.Run() at all, taking the /health endpoint down with it -- exactly the kind of
// DB-dependency-takes-down-liveness failure that endpoint's own comment says it's meant to avoid. A
// SuperAdmin that fails to bootstrap on this attempt will simply be retried on the next restart.
try
{
    await ScoramAPI.Services.SuperAdminBootstrapService.RunAsync(app.Services, app.Logger);
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "SuperAdmin bootstrap check failed at startup (likely: migrations not yet applied). Will retry on next restart.");
}

// One-time, idempotent: gives every pre-existing Exam / Subject / Test / Mock Test / Admin its
// human-readable Business ID (oldest first, deterministic). Runs AFTER the SuperAdmin bootstrap above
// so a freshly-bootstrapped admin is numbered too. Same fail-soft reasoning as the block above --
// it must never stop the process from reaching app.Run() -- and a failed attempt is rolled back
// completely and simply retried on the next restart. Set BusinessIds:AutoBackfillOnStartup=false
// to skip it and run POST /api/admin/business-ids/backfill (dry run first) by hand instead.
try
{
    await ScoramAPI.Services.BusinessIdService.RunStartupBackfillAsync(app.Services, app.Configuration, app.Logger);
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Business ID backfill failed at startup (likely: migrations not yet applied). Nothing was changed; it will be retried on the next restart.");
}

app.Run();