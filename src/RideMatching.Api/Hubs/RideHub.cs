using Microsoft.AspNetCore.SignalR;

namespace RideMatching.Api.Hubs;

/// <summary>
/// Real-time hub for ride updates. Clients join groups to receive scoped events.
/// Contains no business logic; it only manages group membership.
/// </summary>
public sealed class RideHub : Hub
{
    public static string RideGroup(Guid rideId) => $"ride:{rideId}";
    public static string RiderGroup(Guid riderId) => $"rider:{riderId}";
    public static string DriverGroup(Guid driverId) => $"driver:{driverId}";

    /// <summary>Subscribe to updates for a specific ride.</summary>
    public Task SubscribeToRide(Guid rideId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, RideGroup(rideId));

    /// <summary>Subscribe to updates for a specific rider.</summary>
    public Task SubscribeToRider(Guid riderId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, RiderGroup(riderId));

    /// <summary>Subscribe to updates for a specific driver.</summary>
    public Task SubscribeToDriver(Guid driverId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, DriverGroup(driverId));
}
