namespace RideMatching.Api.Domain.Enums;

/// <summary>
/// Lifecycle status of a driver.
/// </summary>
public enum DriverStatus
{
    /// <summary>Driver is not accepting rides and is not tracked in Redis.</summary>
    Offline = 0,

    /// <summary>Driver is online and eligible to be matched to a ride.</summary>
    Available = 1,

    /// <summary>Driver is currently assigned to a ride.</summary>
    Busy = 2
}
