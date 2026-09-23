using RideMatching.Api.Domain.Enums;

namespace RideMatching.Api.Domain.Entities;

/// <summary>
/// A ride request created by a rider. Progresses through the ride state machine
/// and, on success, is assigned to a single driver.
/// </summary>
public class Ride
{
    public Guid Id { get; set; }

    public Guid RiderId { get; set; }

    public double PickupLatitude { get; set; }

    public double PickupLongitude { get; set; }

    public double DestinationLatitude { get; set; }

    public double DestinationLongitude { get; set; }

    public RideStatus Status { get; set; } = RideStatus.Requested;

    /// <summary>Assigned driver, if any. Set atomically during assignment.</summary>
    public Guid? DriverId { get; set; }

    public Driver? Driver { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? MatchedAt { get; set; }

    /// <summary>
    /// Optional idempotency key supplied by the client so repeated create
    /// requests return the same ride instead of creating duplicates.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    public ICollection<RideAssignment> Assignments { get; set; } = new List<RideAssignment>();
}
