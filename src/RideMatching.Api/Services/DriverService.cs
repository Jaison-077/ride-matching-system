using Microsoft.EntityFrameworkCore;
using RideMatching.Api.Data;
using RideMatching.Api.Domain;
using RideMatching.Api.Domain.Entities;
using RideMatching.Api.Domain.Enums;
using RideMatching.Api.Hubs;

namespace RideMatching.Api.Services;

/// <summary>
/// Driver lifecycle operations. SQL Server holds authoritative driver state;
/// Redis is kept in sync for fast discovery and presence.
/// </summary>
public sealed class DriverService
{
    private readonly AppDbContext _db;
    private readonly IRedisLocationService _redis;
    private readonly IRideNotifier _notifier;
    private readonly ILogger<DriverService> _logger;

    public DriverService(
        AppDbContext db,
        IRedisLocationService redis,
        IRideNotifier notifier,
        ILogger<DriverService> logger)
    {
        _db = db;
        _redis = redis;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<Driver> CreateAsync(string name, CancellationToken ct)
    {
        var driver = new Driver
        {
            Id = Guid.NewGuid(),
            Name = name,
            Status = DriverStatus.Offline,
            CreatedAt = DateTime.UtcNow
        };

        _db.Drivers.Add(driver);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("DriverCreated {DriverId} {Name}", driver.Id, driver.Name);
        return driver;
    }

    public async Task<Driver> GoOnlineAsync(Guid driverId, CancellationToken ct)
    {
        var driver = await GetRequiredAsync(driverId, ct);

        driver.Status = DriverStatus.Available;
        driver.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _redis.SetPresenceAsync(driverId, ct);

        _logger.LogInformation("DriverOnline {DriverId}", driverId);
        return driver;
    }

    public async Task<Driver> GoOfflineAsync(Guid driverId, CancellationToken ct)
    {
        var driver = await GetRequiredAsync(driverId, ct);

        driver.Status = DriverStatus.Offline;
        driver.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _redis.RemoveDriverLocationAsync(driverId, ct);
        await _redis.RemovePresenceAsync(driverId, ct);

        _logger.LogInformation("DriverOffline {DriverId}", driverId);
        return driver;
    }

    public async Task<Driver> UpdateLocationAsync(Guid driverId, double latitude, double longitude, CancellationToken ct)
    {
        if (!GeoCoordinates.IsValidLatitude(latitude))
        {
            throw new ValidationException("Latitude must be between -90 and 90.");
        }

        if (!GeoCoordinates.IsValidLongitude(longitude))
        {
            throw new ValidationException("Longitude must be between -180 and 180.");
        }

        var driver = await GetRequiredAsync(driverId, ct);

        driver.Latitude = latitude;
        driver.Longitude = longitude;
        driver.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Only surface online drivers in the discovery index.
        if (driver.Status != DriverStatus.Offline)
        {
            await _redis.UpsertDriverLocationAsync(driverId, latitude, longitude, ct);
            await _redis.RefreshPresenceAsync(driverId, ct);
        }

        _logger.LogInformation(
            "DriverLocationUpdated {DriverId} {Latitude} {Longitude}", driverId, latitude, longitude);

        await _notifier.DriverLocationUpdatedAsync(driverId, latitude, longitude, ct);
        return driver;
    }

    public async Task<Driver?> FindAsync(Guid driverId, CancellationToken ct) =>
        await _db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == driverId, ct);

    private async Task<Driver> GetRequiredAsync(Guid driverId, CancellationToken ct)
    {
        var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.Id == driverId, ct);
        return driver ?? throw new DriverNotFoundException(driverId);
    }
}
