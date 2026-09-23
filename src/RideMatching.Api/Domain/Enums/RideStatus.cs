namespace RideMatching.Api.Domain.Enums;

/// <summary>
/// Lifecycle status of a ride.
/// </summary>
public enum RideStatus
{
    /// <summary>Ride has been created but not yet queued for matching.</summary>
    Requested = 0,

    /// <summary>Ride is queued/being processed by the background matcher.</summary>
    Matching = 1,

    /// <summary>A driver has been assigned to the ride.</summary>
    Matched = 2,

    /// <summary>Ride has completed.</summary>
    Completed = 3,

    /// <summary>Ride was cancelled.</summary>
    Cancelled = 4,

    /// <summary>No available driver could be matched to the ride.</summary>
    NoDriverAvailable = 5
}
