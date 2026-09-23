namespace RideMatching.Api.Configuration;

/// <summary>
/// Matching and Redis tuning values. All bound from the "Matching" configuration
/// section so nothing is hard-coded.
/// </summary>
public sealed class MatchingOptions
{
    public const string SectionName = "Matching";

    /// <summary>Redis GEO search radius, in kilometres.</summary>
    public double SearchRadiusKm { get; set; } = 5d;

    /// <summary>Maximum number of nearby candidate drivers to consider.</summary>
    public int MaxCandidates { get; set; } = 10;

    /// <summary>Time-to-live for a driver's Redis presence key, in seconds.</summary>
    public int PresenceTtlSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum age of a driver's last heartbeat (LastSeenAt) for the driver to be
    /// claimable. A driver whose LastSeenAt is older than this is treated as stale
    /// and cannot be assigned, even if still marked Available in SQL.
    /// </summary>
    public int DriverFreshnessSeconds { get; set; } = 30;

    /// <summary>Redis key for the driver geospatial index.</summary>
    public string GeoKey { get; set; } = "drivers:geo";

    /// <summary>Prefix for per-driver presence keys: {prefix}{driverId}.</summary>
    public string PresenceKeyPrefix { get; set; } = "driver:presence:";
}
