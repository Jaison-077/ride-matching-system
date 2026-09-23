using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using RideMatching.Api.Background;
using RideMatching.Api.Configuration;
using RideMatching.Api.Data;
using RideMatching.Api.Hubs;
using RideMatching.Api.Middleware;
using RideMatching.Api.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration ----
builder.Services.Configure<MatchingOptions>(
    builder.Configuration.GetSection(MatchingOptions.SectionName));
builder.Services.Configure<RedisOptions>(
    builder.Configuration.GetSection(RedisOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("SqlServer")
    ?? "Server=localhost,1433;Database=RideMatching;User Id=sa;Password=Your_password123;TrustServerCertificate=True;";

var redisConnectionString = builder.Configuration
    .GetSection(RedisOptions.SectionName)["ConnectionString"] ?? "localhost:6379";

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

// ---- Background matching queue + worker ----
builder.Services.AddSingleton<IRideMatchingQueue, RideMatchingQueue>();
builder.Services.AddHostedService<RideMatchingWorker>();

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
});

// ---- Health checks ----
builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "sql-server", tags: new[] { "ready" })
    .AddRedis(redisConnectionString, name: "redis", tags: new[] { "ready" });

// ---- Rate limiting (protect frequent driver location updates) ----
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("driver-location", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Request.RouteValues["driverId"]?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromSeconds(1),
                QueueLimit = 0
            }));
});

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
using (var scope = app.Services.CreateScope())
{
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
