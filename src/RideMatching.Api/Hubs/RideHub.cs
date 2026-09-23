using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RideMatching.Api.Data;
using RideMatching.Api.Domain;
using RideMatching.Api.Security;

namespace RideMatching.Api.Hubs;

/// <summary>
/// Real-time hub for ride updates. Requires authentication, and each subscription
/// is authorized: callers may only join groups for resources they own or are
/// assigned to. Contains no business logic beyond that group-membership guard.
/// </summary>
[Authorize]
public sealed class RideHub : Hub
{
    private readonly AppDbContext _db;

    public RideHub(AppDbContext db) => _db = db;

    public static string RideGroup(Guid rideId) => $"ride:{rideId}";
    public static string RiderGroup(Guid riderId) => $"rider:{riderId}";
    public static string DriverGroup(Guid driverId) => $"driver:{driverId}";

    /// <summary>
    /// Subscribe to a specific ride. Allowed only if the caller is the ride's rider
    /// or the driver assigned to it.
    /// </summary>
    public async Task SubscribeToRide(Guid rideId)
    {
        var subjectId = Context.User!.GetSubjectId();

        var ride = await _db.Rides.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == rideId, Context.ConnectionAborted);
        if (ride is null)
        {
            throw new HubException("Ride not found.");
        }

        var isRider = Context.User!.IsInRole(Roles.Rider) && ride.RiderId == subjectId;
        var isAssignedDriver = Context.User!.IsInRole(Roles.Driver) && ride.DriverId == subjectId;
        if (!isRider && !isAssignedDriver)
        {
            throw new HubException("You are not allowed to subscribe to this ride.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RideGroup(rideId));
    }

    /// <summary>Subscribe to a rider's updates. Allowed only for that rider.</summary>
    public async Task SubscribeToRider(Guid riderId)
    {
        if (!(Context.User!.IsInRole(Roles.Rider) && Context.User!.GetSubjectId() == riderId))
        {
            throw new HubException("You are not allowed to subscribe to this rider.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RiderGroup(riderId));
    }

    /// <summary>Subscribe to a driver's updates. Allowed only for that driver.</summary>
    public async Task SubscribeToDriver(Guid driverId)
    {
        if (!(Context.User!.IsInRole(Roles.Driver) && Context.User!.GetSubjectId() == driverId))
        {
            throw new HubException("You are not allowed to subscribe to this driver.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, DriverGroup(driverId));
    }
}
