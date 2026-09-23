namespace RideMatching.Api.Domain.Entities;

/// <summary>
/// Audit trail of a single attempt to assign a driver to a ride.
/// One ride may generate several attempts (failed claims plus one success).
/// </summary>
public class RideAssignment
{
    public Guid Id { get; set; }

    public Guid RideId { get; set; }

    public Ride? Ride { get; set; }

    public Guid DriverId { get; set; }

    public Driver? Driver { get; set; }

    /// <summary>1-based ordinal of this attempt within the ride's matching run.</summary>
    public int AttemptNumber { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True if this attempt won the atomic driver claim.</summary>
    public bool Success { get; set; }
}
