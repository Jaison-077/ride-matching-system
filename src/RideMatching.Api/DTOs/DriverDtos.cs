using System.ComponentModel.DataAnnotations;
using RideMatching.Api.Domain.Entities;

namespace RideMatching.Api.DTOs;

/// <summary>Request to create a new driver.</summary>
public sealed record CreateDriverRequest
{
    /// <example>Driver 1</example>
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;
}

/// <summary>Request to update a driver's location.</summary>
public sealed record UpdateLocationRequest
{
    /// <example>28.6139</example>
    public double Latitude { get; init; }

    /// <example>77.2090</example>
    public double Longitude { get; init; }
}

/// <summary>Public representation of a driver.</summary>
public sealed record DriverResponse
{
    public Guid DriverId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public DateTime? LastSeenAt { get; init; }
    public DateTime CreatedAt { get; init; }

    public static DriverResponse From(Driver d) => new()
    {
        DriverId = d.Id,
        Name = d.Name,
        Status = d.Status.ToString(),
        Latitude = d.Latitude,
        Longitude = d.Longitude,
        LastSeenAt = d.LastSeenAt,
        CreatedAt = d.CreatedAt
    };
}
