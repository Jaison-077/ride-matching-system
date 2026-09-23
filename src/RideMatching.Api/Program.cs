using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using RideMatching.Api.Background;
using RideMatching.Api.Configuration;
using RideMatching.Api.Data;
using RideMatching.Api.Hubs;
using RideMatching.Api.Middleware;
using RideMatching.Api.Security;
using RideMatching.Api.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration ----
builder.Services.Configure<MatchingOptions>(
    builder.Configuration.GetSection(MatchingOptions.SectionName));
builder.Services.Configure<RedisOptions>(
    builder.Configuration.GetSection(RedisOptions.SectionName));
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));

// JWT signing key must come from configuration/environment/secret store.
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32)
{
    throw new InvalidOperationException(
        "Missing or too-short 'Jwt:SigningKey' (needs >= 32 bytes). " +
        "Set it via appsettings.json, environment variables, or a secret store.");
}

// Required connection settings come exclusively from configuration
// (appsettings*.json, environment variables, user secrets, etc.). There are no
// hard-coded fallbacks: fail fast with a clear error if anything is missing.
var connectionString = builder.Configuration.GetConnectionString("SqlServer");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Missing required configuration 'ConnectionStrings:SqlServer'. " +
        "Set it via appsettings.json, environment variables, or a secret store.");
}

var redisConnectionString = builder.Configuration
    .GetSection(RedisOptions.SectionName)
    .GetValue<string>("ConnectionString");
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    throw new InvalidOperationException(
        "Missing required configuration 'Redis:ConnectionString'. " +
        "Set it via appsettings.json, environment variables, or a secret store.");
}

// ---- Persistence ----
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// ---- Redis (shared multiplexer) ----
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var config = ConfigurationOptions.Parse(redisConnectionString);
    config.AbortOnConnectFail = false; // Redis is an optimization; tolerate startup without it.
    return ConnectionMultiplexer.Connect(config);
});

// ---- Application services ----
builder.Services.AddScoped<IRedisLocationService, RedisLocationService>();
builder.Services.AddScoped<DriverService>();
builder.Services.AddScoped<RideService>();
builder.Services.AddScoped<MatchingService>();
builder.Services.AddSingleton<IRideNotifier, SignalRRideNotifier>();
builder.Services.AddSingleton<JwtTokenService>();

// ---- Background matching queue + worker ----
builder.Services.AddSingleton<IRideMatchingQueue, RideMatchingQueue>();
builder.Services.AddHostedService<RideMatchingWorker>();

// ---- Authentication / Authorization ----
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // SignalR sends the token via the access_token query string on the hub path.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/rides"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.RiderOnly, p => p.RequireRole(Roles.Rider));
    options.AddPolicy(Policies.DriverOnly, p => p.RequireRole(Roles.Driver));
});

// ---- SignalR ----
builder.Services.AddSignalR();

// ---- API / Swagger ----
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Real-Time Ride Matching System",
        Version = "v1",
        Description = "Riders request rides; drivers go online and stream location; "
                    + "a background worker performs concurrency-safe nearest-driver matching. "
                    + "Redis discovers nearby drivers, SQL Server claims them, SignalR notifies clients."
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }

    // Bearer auth in Swagger UI.
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the JWT from /api/auth/token/rider or /api/auth/token/driver."
    });
    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", doc)] = new List<string>()
    });
});

// ---- Health checks ----
// Each dependency check is bounded with a short timeout so the readiness probe
// fails fast (Unhealthy) instead of blocking on a dependency's own connect
// timeout when that dependency is down.
var healthCheckTimeout = TimeSpan.FromSeconds(3);
builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "sql-server", tags: new[] { "ready" }, timeout: healthCheckTimeout)
    .AddRedis(redisConnectionString, name: "redis", tags: new[] { "ready" }, timeout: healthCheckTimeout);

// ---- Rate limiting ----
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Frequent driver location updates: per-driver.
    options.AddPolicy("driver-location", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Request.RouteValues["driverId"]?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromSeconds(1),
                QueueLimit = 0
            }));

    // Ride creation: per authenticated rider (falls back to remote IP).
    options.AddPolicy("ride-create", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: RateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromSeconds(10),
                QueueLimit = 0
            }));

    // Driver online/offline toggling: per-driver.
    options.AddPolicy("driver-lifecycle", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Request.RouteValues["driverId"]?.ToString() ?? RateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromSeconds(10),
                QueueLimit = 0
            }));

    // Driver creation: per source IP to limit account/driver spam.
    options.AddPolicy("driver-create", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: RateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromSeconds(10),
                QueueLimit = 0
            }));
});

static string RateLimitPartitionKey(HttpContext httpContext) =>
    httpContext.User?.Identity?.IsAuthenticated == true
        ? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.Identity!.Name ?? "authenticated"
        : httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

var app = builder.Build();

// ---- Pipeline ----
app.UseMiddleware<ExceptionMiddleware>();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Ride Matching API v1");
    c.RoutePrefix = "swagger";
});

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<RideHub>("/hubs/rides");

// ---- Health endpoints ----
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // liveness: process is up, no dependency checks
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// ---- Migrate + seed on startup ----
// In the Testing environment the database provider is swapped (e.g. SQLite) and
// created directly by the test host, so the SQL Server migration is skipped.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    if (app.Environment.IsDevelopment())
    {
        await DbSeeder.SeedAsync(db);
    }
}

await app.RunAsync();

/// <summary>Exposed so the test host (WebApplicationFactory) can reference the entry point.</summary>
public partial class Program { }
