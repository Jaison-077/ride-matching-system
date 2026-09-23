using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RideMatching.Api.Data;
using RideMatching.Api.Security;
using RideMatching.Api.Services;

namespace RideMatching.Tests;

/// <summary>
/// Boots the real API in the "Testing" environment with SQLite (in place of SQL
/// Server) and a fake Redis, so integration tests exercise the genuine auth,
/// authorization, and SignalR pipeline without external infrastructure.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    // Signing key must be >= 32 bytes to satisfy the app's fail-fast check.
    public const string SigningKey = "test-signing-key-test-signing-key-32bytes!";
    public const string Issuer = "ride-matching";
    public const string Audience = "ride-matching";

    private readonly SqliteConnection _connection;
    public FakeRedisLocationService Redis { get; } = new();

    public ApiFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // The app reads required config during its top-level startup statements
        // (before the test host's ConfigureAppConfiguration is applied), so these
        // must be present as environment variables when the host is built.
        Environment.SetEnvironmentVariable("ConnectionStrings__SqlServer",
            "Server=(unused);Database=Test;Trusted_Connection=True;");
        Environment.SetEnvironmentVariable("Redis__ConnectionString", "localhost:6379");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", SigningKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", Audience);
        Environment.SetEnvironmentVariable("Matching__DriverFreshnessSeconds", "30");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Swap AppDbContext to the shared in-memory SQLite connection. Remove all
            // EF Core-related registrations (incl. the SqlServer provider services)
            // so only the SQLite provider remains.
            foreach (var d in services.Where(s =>
                         s.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                         s.ServiceType == typeof(DbContextOptions) ||
                         s.ServiceType == typeof(AppDbContext) ||
                         (s.ServiceType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") ?? false))
                     .ToList())
            {
                services.Remove(d);
            }

            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));

            // Swap Redis: remove the real multiplexer + location service, use the fake.
            RemoveAll(services, typeof(StackExchange.Redis.IConnectionMultiplexer));
            RemoveAll(services, typeof(IRedisLocationService));
            services.AddSingleton<IRedisLocationService>(Redis);

            // Create the schema.
            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        });
    }

    private static void RemoveAll(IServiceCollection services, Type serviceType)
    {
        foreach (var d in services.Where(s => s.ServiceType == serviceType).ToList())
        {
            services.Remove(d);
        }
    }

    /// <summary>Mints a JWT for use as a bearer token in test requests.</summary>
    public string TokenFor(Guid subjectId, string role)
    {
        var opts = Microsoft.Extensions.Options.Options.Create(new RideMatching.Api.Configuration.JwtOptions
        {
            SigningKey = SigningKey,
            Issuer = Issuer,
            Audience = Audience,
            AccessTokenMinutes = 60
        });
        return new JwtTokenService(opts).CreateToken(subjectId, role);
    }

    /// <summary>Runs an action against a fresh DbContext over the same SQLite database.</summary>
    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(db);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
