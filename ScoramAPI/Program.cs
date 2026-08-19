using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ScoramAPI.Data;
using ScoramAPI.Hubs;
using ScoramAPI.Middleware;
using ScoramAPI.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

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

// ---------- CORS (allow the React/Vite frontend during development) ----------
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Must be the very first middleware so it can catch exceptions thrown by anything after it.
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

app.Run();
