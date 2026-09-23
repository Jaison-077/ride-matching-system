namespace RideMatching.Api.Hubs;

/// <summary>
/// Notification abstraction over SignalR. Keeps hub/transport details out of
/// the services and controllers; business code depends only on this contract.
/// </summary>
public interface IRideNotifier
{
    Task RideMatchedAsync(Guid rideId, Guid riderId, Guid driverId, string driverName, CancellationToken ct = default);

    Task RideStatusChangedAsync(Guid rideId, Guid riderId, string status, CancellationToken ct = default);

    Task DriverAssignedAsync(Guid driverId, Guid rideId, CancellationToken ct = default);

    Task DriverLocationUpdatedAsync(Guid driverId, double latitude, double longitude, CancellationToken ct = default);

    Task NoDriverAvailableAsync(Guid rideId, Guid riderId, CancellationToken ct = default);
}
