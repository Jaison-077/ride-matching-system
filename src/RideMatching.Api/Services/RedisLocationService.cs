using Microsoft.Extensions.Options;
using RideMatching.Api.Configuration;
using StackExchange.Redis;

namespace RideMatching.Api.Services;

/// <summary>
/// StackExchange.Redis-backed implementation of driver location and presence.
/// Uses a Redis GEO set for proximity search and short-lived presence keys.
/// All operations degrade gracefully: if Redis is unavailable the caller sees
/// an empty candidate list rather than a corrupted assignment.
/// </summary>
public sealed class RedisLocationService : IRedisLocationService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly MatchingOptions _options;
    private readonly ILogger<RedisLocationService> _logger;

    public RedisLocationService(
        IConnectionMultiplexer redis,
        IOptions<MatchingOptions> options,
        ILogger<RedisLocationService> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }

    private IDatabase Db => _redis.GetDatabase();

    private RedisKey PresenceKey(Guid driverId) =>
        (RedisKey)(_options.PresenceKeyPrefix + driverId.ToString("D"));

    public async Task UpsertDriverLocationAsync(Guid driverId, double latitude, double longitude, CancellationToken ct = default)
    {
        try
        {
            await Db.GeoAddAsync(
                _options.GeoKey,
                longitude,
                latitude,
                driverId.ToString("D"));
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to upsert Redis location for driver {DriverId}", driverId);
        }
    }

    public async Task RemoveDriverLocationAsync(Guid driverId, CancellationToken ct = default)
    {
        try
        {
            await Db.GeoRemoveAsync(_options.GeoKey, driverId.ToString("D"));
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to remove Redis location for driver {DriverId}", driverId);
        }
    }

    public async Task<IReadOnlyList<NearbyDriver>> SearchNearbyAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        try
        {
            var shape = new GeoSearchCircle(_options.SearchRadiusKm, GeoUnit.Kilometers);
            var results = await Db.GeoSearchAsync(
                _options.GeoKey,
                longitude,
                latitude,
                shape,
                count: _options.MaxCandidates,
                demandClosest: true,
                order: Order.Ascending);

            var list = new List<NearbyDriver>(results.Length);
            foreach (var r in results)
            {
                if (Guid.TryParse(r.Member.ToString(), out var id))
                {
                    list.Add(new NearbyDriver(id, r.Distance ?? double.MaxValue));
                }
            }

            return list;
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis GEO search failed; returning no candidates.");
            return Array.Empty<NearbyDriver>();
        }
    }

    public async Task SetPresenceAsync(Guid driverId, CancellationToken ct = default)
    {
        try
        {
            await Db.StringSetAsync(
                PresenceKey(driverId),
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                TimeSpan.FromSeconds(_options.PresenceTtlSeconds));
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to set presence for driver {DriverId}", driverId);
        }
    }

    public Task RefreshPresenceAsync(Guid driverId, CancellationToken ct = default) =>
        SetPresenceAsync(driverId, ct);

    public async Task<bool> IsPresentAsync(Guid driverId, CancellationToken ct = default)
    {
        try
        {
            return await Db.KeyExistsAsync(PresenceKey(driverId));
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to check presence for driver {DriverId}", driverId);
            // Fail-open: SQL Server still guards the actual claim, so presence
            // uncertainty must not block an otherwise-valid assignment.
            return true;
        }
    }

    public async Task RemovePresenceAsync(Guid driverId, CancellationToken ct = default)
    {
        try
        {
            await Db.KeyDeleteAsync(PresenceKey(driverId));
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to remove presence for driver {DriverId}", driverId);
        }
    }
}
