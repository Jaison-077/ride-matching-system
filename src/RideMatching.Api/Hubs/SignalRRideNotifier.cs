using Microsoft.AspNetCore.SignalR;

namespace RideMatching.Api.Hubs;

/// <summary>
/// SignalR-backed <see cref="IRideNotifier"/>. Fans events out to the relevant
/// ride/rider/driver groups. Notification failures are swallowed so that
/// authoritative state (already committed in SQL Server) is never rolled back
/// because a client could not be reached.
/// </summary>
public sealed class SignalRRideNotifier : IRideNotifier
{
    private readonly IHubContext<RideHub> _hub;
    private readonly ILogger<SignalRRideNotifier> _logger;

    public SignalRRideNotifier(IHubContext<RideHub> hub, ILogger<SignalRRideNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public Task RideMatchedAsync(Guid rideId, Guid riderId, Guid driverId, string driverName, CancellationToken ct = default) =>
        SafeSendAsync(async () =>
        {
            var payload = new { rideId, riderId, driverId, driverName };
            await _hub.Clients.Group(RideHub.RideGroup(rideId)).SendAsync("RideMatched", payload, ct);
            await _hub.Clients.Group(RideHub.RiderGroup(riderId)).SendAsync("RideMatched", payload, ct);
        }, "RideMatched", rideId);

    public Task RideStatusChangedAsync(Guid rideId, Guid riderId, string status, CancellationToken ct = default) =>
        SafeSendAsync(async () =>
        {
            var payload = new { rideId, riderId, status };
            await _hub.Clients.Group(RideHub.RideGroup(rideId)).SendAsync("RideStatusChanged", payload, ct);
            await _hub.Clients.Group(RideHub.RiderGroup(riderId)).SendAsync("RideStatusChanged", payload, ct);
        }, "RideStatusChanged", rideId);

    public Task DriverAssignedAsync(Guid driverId, Guid rideId, CancellationToken ct = default) =>
        SafeSendAsync(() =>
            _hub.Clients.Group(RideHub.DriverGroup(driverId))
                .SendAsync("DriverAssigned", new { driverId, rideId }, ct),
            "DriverAssigned", rideId);

    public Task DriverLocationUpdatedAsync(Guid driverId, double latitude, double longitude, CancellationToken ct = default) =>
        SafeSendAsync(() =>
            _hub.Clients.Group(RideHub.DriverGroup(driverId))
                .SendAsync("DriverLocationUpdated", new { driverId, latitude, longitude }, ct),
            "DriverLocationUpdated", driverId);

    public Task NoDriverAvailableAsync(Guid rideId, Guid riderId, CancellationToken ct = default) =>
        SafeSendAsync(async () =>
        {
            var payload = new { rideId, riderId };
            await _hub.Clients.Group(RideHub.RideGroup(rideId)).SendAsync("NoDriverAvailable", payload, ct);
            await _hub.Clients.Group(RideHub.RiderGroup(riderId)).SendAsync("NoDriverAvailable", payload, ct);
        }, "NoDriverAvailable", rideId);

    private async Task SafeSendAsync(Func<Task> send, string eventName, Guid id)
    {
        try
        {
            await send();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast {Event} for {Id}", eventName, id);
        }
    }
}
