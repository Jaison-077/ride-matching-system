using RideMatching.Api.Domain.Enums;

namespace RideMatching.Api.Domain.Entities;

/// <summary>
/// A driver that can be matched to rides. SQL Server is the authoritative source
/// of driver state; Redis only holds hot location/presence data.
/// </summary>
public class Driver
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DriverStatus Status { get; set; } = DriverStatus.Offline;

    /// <summary>Last known latitude persisted to SQL Server.</summary>
    public double? Latitude { get; set; }

    /// <summary>Last known longitude persisted to SQL Server.</summary>
    public double? Longitude { get; set; }

    /// <summary>UTC timestamp of the most recent location update / heartbeat.</summary>
    public DateTime? LastSeenAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
