namespace RideMatching.Api.Services;

/// <summary>A nearby driver candidate returned from a Redis GEO search.</summary>
/// <param name="DriverId">The candidate driver id.</param>
/// <param name="DistanceKm">Distance from the search origin, in kilometres.</param>
public readonly record struct NearbyDriver(Guid DriverId, double DistanceKm);

/// <summary>
/// Abstraction over Redis for driver location and presence. Redis answers
/// "which drivers are nearby?" and tracks live presence; it is never the
/// authority for whether a driver can actually be assigned.
/// </summary>
public interface IRedisLocationService
{
    Task UpsertDriverLocationAsync(Guid driverId, double latitude, double longitude, CancellationToken ct = default);

    Task RemoveDriverLocationAsync(Guid driverId, CancellationToken ct = default);

    /// <summary>Nearby available candidates ordered nearest-first, capped at MaxCandidates.</summary>
    Task<IReadOnlyList<NearbyDriver>> SearchNearbyAsync(double latitude, double longitude, CancellationToken ct = default);

    Task SetPresenceAsync(Guid driverId, CancellationToken ct = default);

    Task RefreshPresenceAsync(Guid driverId, CancellationToken ct = default);

    Task<bool> IsPresentAsync(Guid driverId, CancellationToken ct = default);

    Task RemovePresenceAsync(Guid driverId, CancellationToken ct = default);
}
