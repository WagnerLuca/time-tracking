using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using TimeTracking.Api.Data;
using TimeTracking.Api.Filters;
using TimeTracking.Api.Middleware;
using TimeTracking.Api.Models;
using TimeTracking.Api.Services;

// `dotnet TimeTracking.Api.dll generate-vapid-keys` prints a fresh VAPID key pair for Web Push
if (args.Contains("generate-vapid-keys"))
{
    var (vapidPublic, vapidPrivate) = VapidKeys.Generate();
    Console.WriteLine($"WebPush__PublicKey={vapidPublic}");
    Console.WriteLine($"WebPush__PrivateKey={vapidPrivate}");
    return;
}

var builder = WebApplication.CreateBuilder(args);

// `dotnet TimeTracking.Api.dll perf-seed --employees 100 --years 10` fills the Postgres database with a
// synthetic multi-year history for performance measurements (PerfSeeder, docs/PERFORMANCE_PLAN.md)
if (args.FirstOrDefault() == "perf-seed")
{
    Environment.ExitCode = await PerfSeedCommand.RunAsync(args, builder.Configuration, builder.Environment);
    return;
}

// QuestPDF is used for generating monthly/yearly member PDF reports.
// Community license: free for organizations with < $1M USD annual gross revenue.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Configure Serilog from appsettings.json
// Sinks are config-driven: to add Grafana Loki later, just install
// Serilog.Sinks.Grafana.Loki and add a "Loki" section under Serilog:WriteTo.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "TimeTracking.Api"));

// Add services to the container.
builder.Services.AddMemoryCache();
builder.Services.AddControllers(options =>
{
    // Reject requests from deactivated users on every authenticated request
    options.Filters.Add<ActiveUserFilter>();
})
// Undefined enum values (e.g. role 3, "above" Owner) are a 400, not silently accepted
.AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new TimeTracking.Api.Infrastructure.StrictEnumConverterFactory()));
builder.Services.AddEndpointsApiExplorer();

// Add API Versioning
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "Time Tracking API",
        Version = "v1",
        Description = "REST API for time tracking, organizations, work schedules, absences and holidays."
    });

    // Include XML comments from the generated documentation file
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    c.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter JWT with Bearer into field",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", doc),
            new List<string>()
        }
    });
});

// Configure JWT settings
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>();

if (jwtSettings == null || string.IsNullOrEmpty(jwtSettings.Secret))
{
    throw new InvalidOperationException("JWT settings are not properly configured.");
}

// Outside development/tests, refuse to run with the shipped placeholder or a weak secret
var isDevOrTest = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
if (!isDevOrTest && (jwtSettings.Secret.StartsWith("CHANGE_THIS", StringComparison.OrdinalIgnoreCase)
                     || Encoding.UTF8.GetByteCount(jwtSettings.Secret) < 32))
{
    throw new InvalidOperationException(
        "JwtSettings:Secret is the placeholder or shorter than 32 bytes. Set JwtSettings__Secret to a random value, e.g. `openssl rand -base64 48`.");
}

// Add Authentication
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
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
        ClockSkew = TimeSpan.Zero // Remove default 5 minute tolerance
    };

    options.Events = new JwtBearerEvents
    {
        // Never log tokens or Authorization headers; an expired token is routine, not an error
        OnAuthenticationFailed = context =>
        {
            if (context.Exception is SecurityTokenExpiredException)
                context.Response.Headers.Append("Token-Expired", "true");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// Register application services
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ITotpService, TotpService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Email: SMTP when configured; otherwise either log or discard mail according to configuration.
var emailSettings = builder.Configuration.GetSection("Email").Get<EmailSettings>() ?? new EmailSettings();
builder.Services.AddSingleton(emailSettings);
if (string.IsNullOrWhiteSpace(emailSettings.Smtp.Host))
{
    if (emailSettings.LogWhenSmtpUnconfigured)
        builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
    else
        builder.Services.AddSingleton<IEmailSender, DiscardEmailSender>();
}
else
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
builder.Services.AddScoped<IInvitationService, InvitationService>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<ITimeTrackingService, TimeTrackingService>();
builder.Services.AddScoped<MemberEntryService>();
builder.Services.AddScoped<ClosedPeriodService>();
builder.Services.AddScoped<AccountingService>();
builder.Services.AddScoped<PayrollExportService>();
builder.Services.AddScoped<ITimeEntryExportService, TimeEntryExportService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IRequestService, RequestService>();
builder.Services.AddScoped<IHolidayService, HolidayService>();
builder.Services.AddScoped<IAbsenceDayService, AbsenceDayService>();
builder.Services.AddScoped<AppointmentService>();
builder.Services.AddScoped<IWorkScheduleService, WorkScheduleService>();
builder.Services.AddScoped<IPauseRuleService, PauseRuleService>();
builder.Services.AddScoped<IGroupService, GroupService>();
builder.Services.AddScoped<IPdfReportService, PdfReportService>();
builder.Services.AddHostedService<RefreshTokenCleanupService>();
builder.Services.AddHostedService<FailureEntryService>();
builder.Services.AddHostedService<BalanceCheckpointWarmupService>();

// Web Push (issue #67): keys from WebPush:* (env WebPush__PublicKey/PrivateKey/Subject); in
// Development a key pair is created once in the gitignored vapid-keys.dev.json. Without keys
// push stays off (the public key endpoint returns null).
var webPush = builder.Configuration.GetSection("WebPush").Get<WebPushSettings>() ?? new WebPushSettings();
if (!webPush.IsConfigured && builder.Environment.IsDevelopment())
    (webPush.PublicKey, webPush.PrivateKey) = VapidKeys.LoadOrCreate(Path.Combine(builder.Environment.ContentRootPath, "vapid-keys.dev.json"));
// The push services want a contact: the https site, else the sender address of the mails
if (string.IsNullOrWhiteSpace(webPush.Subject))
    webPush.Subject = emailSettings.FrontendBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        ? emailSettings.FrontendBaseUrl
        : "mailto:" + (System.Net.Mail.MailAddress.TryCreate(emailSettings.From, out var sender) ? sender.Address : "no-reply@localhost");
if (!webPush.Subject.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) && !webPush.Subject.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("WebPush:Subject must be a mailto: or https: URI (env WebPush__Subject).");
builder.Services.AddSingleton(webPush);
builder.Services.AddSingleton<PushQueue>();
builder.Services.AddHttpClient<IPushSender, WebPushSender>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<PushService>();
builder.Services.AddHostedService<PushDispatcher>();

// Add Entity Framework - PostgreSQL or InMemory
var useInMemory = builder.Configuration.GetValue<bool>("UseInMemoryDatabase");
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<RlsContext>();
builder.Services.AddScoped<RlsConnectionInterceptor>();
builder.Services.AddDbContext<TimeTrackingDbContext>((sp, options) =>
{
    if (useInMemory)
    {
        options.UseInMemoryDatabase("TimeTrackingDb");
    }
    else
    {
        // DATABASE_URL (postgres:// or postgresql://) wins, else ConnectionStrings:DefaultConnection
        var connectionString = DatabaseUrl.ToConnectionString(Environment.GetEnvironmentVariable("DATABASE_URL"))
                               ?? builder.Configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Database connection string not found. Set DATABASE_URL, ConnectionStrings:DefaultConnection, or set UseInMemoryDatabase=true.");
        }

        options.UseNpgsql(connectionString);
        // Row-level security: every connection carries the acting user (see RowLevelSecurity.cs)
        options.AddInterceptors(sp.GetRequiredService<RlsConnectionInterceptor>());
    }
});

// Add CORS — restrict origins in production, allow any in development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowOrigins", policy =>
    {
        string[]? configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        if (configuredOrigins != null && configuredOrigins.Length > 0)
        {
            policy.WithOrigins(configuredOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
        }
        else if (builder.Environment.IsDevelopment())
        {
            // Development convenience only — production must list its origins explicitly
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

// Add Rate Limiting — partitioned per client IP (auth) or per user (data access), so one
// noisy client cannot exhaust everybody else's budget
// General permits per user and minute; appsettings.Development.json raises it so a full e2e run fits
var generalPerMinute = builder.Configuration.GetValue("RateLimiting:GeneralPerMinute", 300);
// Logins per IP and minute; only appsettings.Development.json raises it (e2e runs log in as several users)
var authStrictPerMinute = builder.Configuration.GetValue("RateLimiting:AuthStrictPerMinute", Program.DefaultAuthStrictPerMinute);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // IPv6 clients usually control a whole /64, so they count as one client per /64
    static string ClientIp(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip == null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? Convert.ToHexString(ip.GetAddressBytes(), 0, 8) + "::/64"
            : ip.ToString();
    }
    static string UserOrIp(HttpContext ctx) =>
        ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value is { } id ? $"user:{id}" : $"ip:{ClientIp(ctx)}";
    static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0
    };

    // Strict policy for login/register/2FA (brute-force & credential stuffing), per IP
    options.AddPolicy("AuthStrict", ctx => RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => Window(authStrictPerMinute)));

    // Moderate policy for token refresh & other auth actions, per IP
    options.AddPolicy("AuthModerate", ctx => RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => Window(30)));

    // General policy for data-access endpoints (prevent rapid enumeration), per user
    options.AddPolicy("General", ctx => RateLimitPartition.GetFixedWindowLimiter(UserOrIp(ctx), _ => Window(generalPerMinute)));

    // Terminal punches, per API key (hashed, so the key never sits in limiter state), else per IP.
    // Unknown-card guessing has its own, tighter budget in ApiKeyService.
    options.AddPolicy("Terminal", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Request.Headers[TimeTracking.Api.Controllers.ApiKeysController.ApiKeyHeader].ToString() is { Length: > 0 } key
            ? "key:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key.Trim())))
            : $"ip:{ClientIp(ctx)}",
        _ => Window(120)));
});

// Behind a reverse proxy, take the client IP from X-Forwarded-For — only when explicitly
// enabled, otherwise any client could spoof its IP and dodge the rate limits
var trustForwardedHeaders = builder.Configuration.GetValue<bool>("ReverseProxy:TrustForwardedHeaders");
if (trustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // Only the configured proxy network may set X-Forwarded-For; anything else would let
        // clients pick their own IP and dodge the rate limits
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
        foreach (var cidr in builder.Configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
        if (o.KnownIPNetworks.Count == 0)
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("127.0.0.0/8"));
    });
}

var app = builder.Build();

// Auto-migrate database on startup
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<TimeTrackingDbContext>();

        if (app.Configuration.GetValue<bool>("UseInMemoryDatabase"))
        {
            // InMemory database - just ensure created (no migrations)
            context.Database.EnsureCreated();
        }
        else
        {
            // Apply all pending migrations (this will also create the database if needed)
            context.Database.Migrate();
        }

        // Demo data (users with known passwords!) only for the in-memory dev database or when asked for
        if (app.Configuration.GetValue<bool>("UseInMemoryDatabase") || app.Configuration.GetValue<bool>("SeedDemoData"))
            await DbSeeder.SeedAsync(context);

        // Issue #81: months closed before frozen figures existed are frozen once, from the data as it is now
        await FrozenFigures.FreezeUnfrozenAsync(context, services.GetRequiredService<ILogger<Program>>());
        // Issue #82: work schedules in the model (contiguous periods, one future one at most) — after the freeze, so closed
        // months keep their figures; idempotent, logs what it changed
        await WorkSchedulePlan.NormalizeAllAsync(context, services.GetRequiredService<ILogger<Program>>());
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogCritical(ex, "An error occurred while migrating or seeding the database.");
        // Never serve requests on a half-migrated schema
        throw;
    }
}

// Configure the HTTP request pipeline.
if (trustForwardedHeaders)
    app.UseForwardedHeaders();

app.UseMiddleware<ExceptionHandlingMiddleware>();

// Serilog HTTP request logging (replaces default Microsoft request logging)
app.UseSerilogRequestLogging();

// API docs only in development unless explicitly enabled (Swagger:Enabled)
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Enable CORS
app.UseCors("AllowOrigins");

// Disable HTTPS redirection in development for testing
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Authentication & Authorization middleware (order matters!)
app.UseAuthentication();
// Rate limiting after authentication: the per-user "General" partition reads ctx.User
// (before it, every caller fell back to the shared per-IP budget). Disabled in Testing.
if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseRateLimiter();
}
app.UseAuthorization();

app.MapControllers();

// Health check endpoint — 503 when the database is unreachable
app.MapGet("/health", async (TimeTrackingDbContext db, CancellationToken ct) =>
{
    var dbOk = await db.Database.CanConnectAsync(ct);
    var body = new { status = dbOk ? "healthy" : "unhealthy", database = dbOk, timestamp = DateTime.UtcNow, service = "dotnet-time-tracking-backend" };
    return dbOk ? Results.Ok(body) : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();

/// <summary>
/// Partial class declaration to make Program accessible for WebApplicationFactory in integration tests.
/// </summary>
public partial class Program
{
    /// <summary>Login/register/2FA attempts per IP and minute unless <c>RateLimiting:AuthStrictPerMinute</c> overrides it.</summary>
    public const int DefaultAuthStrictPerMinute = 10;
}
