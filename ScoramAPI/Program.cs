
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
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
var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(renderPort) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls($"http://+:{renderPort}");
}

// ---------- Structured logging ----------
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration));

// ---------- Database ----------
// SlowQueryInterceptor observes finished database commands.
// EnableRetryOnFailure (opt-in via Database:EnableRetryOnFailure) retries transient SQL connection failures.
builder.Services.AddSingleton<SlowQueryInterceptor>();

builder.Services.AddDbContext<ScoramDbContext>((serviceProvider, options) =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql =>
        {
            // Retry-on-failure is OFF by default. SqlServerRetryingExecutionStrategy rejects every
            // user-initiated transaction that isn't wrapped in CreateExecutionStrategy().ExecuteAsync(),
            // and this codebase opens manual transactions in many places (bulk import commit, exam
            // merge, account deletion, test import, app config, and DbContext.SaveChangesAsync's own
            // Business-ID transaction). With it on they all fail with "does not support user-initiated
            // transactions". Set "Database:EnableRetryOnFailure": true only after every
            // BeginTransactionAsync site has been wrapped in the strategy.
            if (builder.Configuration.GetValue<bool>("Database:EnableRetryOnFailure"))
            {
                sql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null
                );
            }
        })
    .AddInterceptors(
        serviceProvider.GetRequiredService<SlowQueryInterceptor>()
    )
);

// GET /health/ready
builder.Services.AddSingleton<ReadinessCheck>();

// ---------- Response compression ----------
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "application/problem+json" });
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);

// ---------- Services ----------
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddSingleton<ITotpService, TotpService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<ISecurityStampService, SecurityStampService>();
builder.Services.AddScoped<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IAdminPermissionService, AdminPermissionService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPushNotificationService, PushNotificationService>();

builder.Services.AddSingleton<INotificationWorkQueue, NotificationWorkQueue>();
builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddScoped<INotificationFanOutService, NotificationFanOutService>();

builder.Services.AddScoped<IAppConfigService, AppConfigService>();
builder.Services.AddScoped<IAdminAnalyticsService, AdminAnalyticsService>();

builder.Services.AddScoped<IStudentProgressService, StudentProgressService>();

builder.Services.Configure<ScoramAPI.Services.Analytics.AnalyticsOptions>(
    builder.Configuration.GetSection(ScoramAPI.Services.Analytics.AnalyticsOptions.SectionName));

builder.Services.AddScoped<IProgressAnalyticsService, ProgressAnalyticsService>();
builder.Services.AddScoped<IRevisionService, RevisionService>();
builder.Services.AddScoped<IStudyPlanService, StudyPlanService>();
builder.Services.AddSingleton<IDmViewingService, DmViewingService>();
builder.Services.AddScoped<IAccountDeletionService, AccountDeletionService>();
builder.Services.AddScoped<IMsg91Service, Msg91Service>();
builder.Services.AddSingleton<ICaptchaService, CaptchaService>();

builder.Services.AddScoped<IBulkImportService, BulkImportService>();
builder.Services.AddScoped<IBulkPaperImportService, BulkPaperImportService>();
builder.Services.AddScoped<IQuestionBankImportService, QuestionBankImportService>();
builder.Services.AddScoped<IBulkUploadZipService, BulkUploadZipService>();
builder.Services.AddHostedService<BulkImportStagingCleanupService>();
builder.Services.AddHostedService<PrivateAttachmentCleanupService>();
builder.Services.AddScoped<IQuestionBankMirrorService, QuestionBankMirrorService>();
builder.Services.AddScoped<IBulkImportCommitService, BulkImportCommitService>();
builder.Services.AddScoped<IBulkPaperImportCommitService, BulkPaperImportCommitService>();
builder.Services.AddScoped<IQuestionBankImportCommitService, QuestionBankImportCommitService>();
builder.Services.AddScoped<ITestBulkImportService, TestBulkImportService>();
builder.Services.AddScoped<ITestAttemptService, TestAttemptService>();
builder.Services.AddScoped<ISubjectManagementService, SubjectManagementService>();
builder.Services.AddScoped<IExamTaxonomyService, ExamTaxonomyService>();
builder.Services.AddScoped<IBusinessIdService, BusinessIdService>();
builder.Services.AddScoped<IGamificationService, GamificationService>();
builder.Services.AddScoped<IMyExamScopeService, MyExamScopeService>();

builder.Services.AddScoped<IBulkQuestionEditService, BulkQuestionEditService>();
builder.Services.AddScoped<IStimulusLinkService, StimulusLinkService>();
builder.Services.AddScoped<IPaperContentService, PaperContentService>();

// ---------- Redis (optional) ----------
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
        Console.Error.WriteLine($"Redis configured but unreachable at startup, falling back to single-instance-only behavior: {ex.Message}");
    }
}

if (redisMultiplexer != null)
{
    builder.Services.AddSingleton(redisMultiplexer);
    builder.Services.AddSingleton<IChatPresenceService, RedisChatPresenceService>();
    builder.Services.AddSingleton<IDmPresenceService, RedisDmPresenceService>();
    builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redisConnectionString);
    builder.Services.AddSingleton<IBackgroundJobQueue, RedisBackgroundJobQueue>();
    builder.Services.AddHostedService<SearchIndexWorker>();
    builder.Services.AddHostedService<BulkImportCommitWorker>();
    builder.Services.AddHostedService<BulkPaperImportCommitWorker>();
    builder.Services.AddHostedService<QuestionBankImportCommitWorker>();
}
else
{
    builder.Services.AddSingleton<IChatPresenceService, ChatPresenceService>();
    builder.Services.AddSingleton<IDmPresenceService, DmPresenceService>();
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddSingleton<IBackgroundJobQueue, NullBackgroundJobQueue>();
}

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IInstantSearchService, InstantSearchService>();
builder.Services.AddScoped<IFallbackSearchService, FallbackSearchService>();
builder.Services.AddSingleton<IStagedDataCache, StagedDataCache>();

// ---------- Azure Blob Storage ----------
var blobConn = builder.Configuration["BlobStorage:ConnectionString"];

Console.WriteLine($"BLOB CONFIG CHECK: {(string.IsNullOrWhiteSpace(blobConn) ? "MISSING" : "AVAILABLE")}");
builder.Services.AddSingleton(_ => new BlobServiceClient(builder.Configuration["BlobStorage:ConnectionString"]));
builder.Services.AddScoped<IAzureBlobService, AzureBlobService>();

// ---------- Rate limiting ----------
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ClientIpResolver.PartitionKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    options.AddPolicy("refresh", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ClientIpResolver.PartitionKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    options.AddPolicy("register", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ClientIpResolver.PartitionKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0
        }));

    options.AddPolicy("content-post", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetRateLimitPartitionKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    options.AddPolicy("smart-progress-write", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetRateLimitPartitionKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    options.AddPolicy("vote", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetRateLimitPartitionKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    options.AddPolicy("report", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetRateLimitPartitionKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0
        }));

    options.AddPolicy("feedback", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetRateLimitPartitionKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0
        }));

    options.AddPolicy("review-write", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetRateLimitPartitionKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0
        }));

    options.AddPolicy("public-read", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetRateLimitPartitionKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

static string GetRateLimitPartitionKey(HttpContext httpContext)
{
    if (httpContext.User.Identity?.IsAuthenticated == true)
    {
        var sub = httpContext.User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
                  ?? httpContext.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (sub != null) return $"user:{sub}";
    }

    return $"ip:{ClientIpResolver.PartitionKey(httpContext)}";
}

// ---------- Controllers & Swagger ----------
builder.Services.AddControllers(options =>
        options.Filters.Add<ScoramAPI.Middleware.MustChangePasswordFilter>())
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

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

// ---------- SignalR ----------
var signalRBuilder = builder.Services.AddSignalR();

if (redisMultiplexer != null)
{
    signalRBuilder.AddStackExchangeRedis(redisConnectionString!);
}

// ---------- JWT Authentication ----------
var jwtSection = builder.Configuration.GetSection("Jwt");

if (string.IsNullOrWhiteSpace(jwtSection["Key"]) ||
    Encoding.UTF8.GetByteCount(jwtSection["Key"]!) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key is missing or shorter than 32 bytes. Set it via configuration / the Jwt__Key environment variable.");
}

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
        ClockSkew = TimeSpan.FromSeconds(30),
        ValidIssuer = jwtSection["Issuer"],
        ValidAudience = jwtSection["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!))
    };

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

        OnTokenValidated = async context =>
        {
            var principal = context.Principal;

            var subClaim = principal?.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
                           ?? principal?.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);

            var stampClaim = principal?.FindFirst("stamp")?.Value;

            if (subClaim == null || stampClaim == null || !Guid.TryParse(subClaim, out var principalId))
            {
                context.Fail("Token is missing required claims.");
                return;
            }

            var isAdmin = principal!.IsInRole("Admin") || principal.IsInRole("SuperAdmin");

            var stampService = context.HttpContext.RequestServices
                .GetRequiredService<ScoramAPI.Services.ISecurityStampService>();

            if (!await stampService.IsValidAsync(principalId, isAdmin, stampClaim))
            {
                context.Fail("Token has been revoked.");
                return;
            }

            // SINGLE-DEVICE LOGIN -- students only (isAdmin accounts never carry a "sid" claim -- see
            // TokenService.GenerateAdminToken and AdminSessionsController for why admins are exempt).
            // A token with no "sid" claim at all was issued before this student's first post-deployment
            // login (see the AddSingleDeviceLogin migration's comment) and is intentionally NOT checked
            // here -- this is what keeps existing sessions alive across the deployment of this feature.
            var sidClaim = principal.FindFirst("sid")?.Value;
            if (!isAdmin && sidClaim != null)
            {
                if (!await stampService.IsSessionValidAsync(principalId, sidClaim))
                {
                    // Distinguished from the generic "Token has been revoked." above via
                    // HttpContext.Items so the OnChallenge handler below can give the client the
                    // specific, machine-readable SESSION_REVOKED code instead of a bodyless 401 --
                    // every OTHER revocation reason (expired token, logout, password change,
                    // deactivation, the stamp check above) deliberately keeps producing that same
                    // bodyless 401 it always has.
                    context.HttpContext.Items["AuthFailureCode"] = "SESSION_REVOKED";
                    context.Fail("Session has been superseded by a login on another device.");
                }
            }
        }
    };

    options.Events.OnChallenge = context =>
    {
        if (context.HttpContext.Items["AuthFailureCode"] is "SESSION_REVOKED")
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync(
                "{\"code\":\"SESSION_REVOKED\",\"message\":\"Your account was logged in on another device.\"}");
        }

        // Every other 401 (missing/expired/malformed token, SecurityStamp mismatch from logout,
        // password change, deactivation, etc.) deliberately does NOT call context.HandleResponse()
        // here, so JwtBearerHandler's own default challenge logic still runs afterward -- the same
        // bodyless 401 + WWW-Authenticate header clients have always gotten for those.
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization();

// ---------- CORS ----------
var configuredCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
var devCorsOrigins = new[] { "http://localhost:5173", "http://localhost:3000", "https://scoram.in" };

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
                  .WithExposedHeaders("Content-Disposition", "X-Report-Truncated")
                  .AllowCredentials();
        }
    });
});

var app = builder.Build();

// ---------- Forwarded headers / effective client IP ----------
var clientIpOptions = builder.Configuration.GetSection(ClientIpOptions.SectionName)
    .Get<ClientIpOptions>() ?? new ClientIpOptions();

if (clientIpOptions.HasTrustedProxies)
{
    clientIpOptions.ToForwardedHeadersOptions();
}

if (string.Equals(
        Environment.GetEnvironmentVariable("ASPNETCORE_FORWARDEDHEADERS_ENABLED"),
        "true",
        StringComparison.OrdinalIgnoreCase))
{
    app.Logger.LogWarning(
        "SEC-004: ASPNETCORE_FORWARDEDHEADERS_ENABLED=true is set. That trusts X-Forwarded-* from ANY caller ahead of the ClientIp settings; unset it.");
}

if (!clientIpOptions.HasTrustedProxies && !app.Environment.IsDevelopment())
{
    app.Logger.LogWarning(
        "SEC-004: ClientIp:TrustedProxies/TrustedNetworks are not configured; running the INTERIM client-IP rule (forwarded headers only from private-network peers, rightmost X-Forwarded-For entry minus ClientIp:SkipRightmostEntries={Skip}).",
        clientIpOptions.SkipRightmostEntries);
}

app.UseClientIpResolution(clientIpOptions);

app.UseResponseCompression();
app.UseMiddleware<RequestTimingMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();

// ---------- Swagger ----------
var swaggerEnabled = app.Environment.IsDevelopment()
    || builder.Configuration.GetValue<bool>("Swagger:EnableInProduction");

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ---------- Static files and upload privacy ----------
app.UseMiddleware<ScoramAPI.Middleware.UploadsPrivacyMiddleware>();
app.UseStaticFiles();

app.UseCors("FrontendDev");
app.UseHttpsRedirection();
app.UseAuthentication();

app.UseMiddleware<ScoramAPI.Middleware.MaintenanceModeMiddleware>();

// ---------- My Exams scoped public routes ----------
var myExamsScopedPublicPrefixes = new[]
{
    "/api/papers", "/api/question-bank", "/api/questions", "/api/mocktests",
    "/api/practice-tests/templates", "/api/discussions"
};

app.Use(async (context, next) =>
{
    var hasBearer = context.Request.Headers.Authorization.ToString()
        .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

    if (hasBearer && context.User.Identity?.IsAuthenticated != true)
    {
        var path = context.Request.Path;

        if (myExamsScopedPublicPrefixes.Any(prefix =>
                path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";

            // SINGLE-DEVICE LOGIN -- this middleware runs between UseAuthentication() and
            // UseAuthorization(), so for these specific "scoped public" prefixes it answers the 401
            // itself and returns before the JwtBearerEvents.OnChallenge handler above ever gets a
            // chance to run (that handler only fires via UseAuthorization()'s ChallengeAsync). Without
            // this check, a device revoked by a newer login would see the same generic message as any
            // other auth failure on exactly the routes (Question Bank, papers, exams) it's most likely
            // to be actively polling -- so mirror OnChallenge's AuthFailureCode check here. Every other
            // reason this branch is reached (missing token, expired token, SecurityStamp mismatch from
            // logout/password change/deactivation) keeps the original generic message, unchanged.
            await context.Response.WriteAsync(
                context.Items["AuthFailureCode"] is "SESSION_REVOKED"
                    ? "{\"code\":\"SESSION_REVOKED\",\"message\":\"Your account was logged in on another device.\"}"
                    : "{\"message\":\"Your session has expired. Please sign in again.\"}");

            return;
        }
    }

    await next();
});

app.UseRateLimiter();
app.UseAuthorization();

// ---------- Health endpoints ----------
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/health/ready", async (
    ReadinessCheck readiness,
    CancellationToken cancellationToken) =>
{
    var result = await readiness.CheckAsync(cancellationToken);

    return Results.Json(
        new
        {
            status = result.Ready ? "ready" : "unavailable",
            database = result.Database
        },
        statusCode: result.Ready
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable);
});

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

// ---------- SuperAdmin bootstrap ----------
try
{
    await ScoramAPI.Services.SuperAdminBootstrapService.RunAsync(app.Services, app.Logger);
}
catch (Exception ex)
{
    app.Logger.LogError(
        ex,
        "SuperAdmin bootstrap check failed at startup (likely: migrations not yet applied). Will retry on next restart.");
}

// ---------- Business ID startup backfill ----------
try
{
    await ScoramAPI.Services.BusinessIdService.RunStartupBackfillAsync(
        app.Services,
        app.Configuration,
        app.Logger);
}
catch (Exception ex)
{
    app.Logger.LogError(
        ex,
        "Business ID backfill failed at startup (likely: migrations not yet applied). Nothing was changed; it will be retried on the next restart.");
}

app.Run();
