using System.ComponentModel.DataAnnotations;
using RideMatching.Api.Domain.Entities;

namespace RideMatching.Api.DTOs;

/// <summary>Request to create a ride.</summary>
public sealed record CreateRideRequest
{
    [Required]
    public Guid RiderId { get; init; }

    /// <example>28.6139</example>
    public double PickupLatitude { get; init; }

    /// <example>77.2090</example>
    public double PickupLongitude { get; init; }

    /// <example>28.5355</example>
    public double DestinationLatitude { get; init; }

    /// <example>77.3910</example>
    public double DestinationLongitude { get; init; }
}

/// <summary>Immediate response returned when a ride is accepted for matching.</summary>
public sealed record CreateRideResponse
{
    public Guid RideId { get; init; }
    public string Status { get; init; } = string.Empty;
}

/// <summary>Coordinate pair used in ride responses.</summary>
public sealed record LocationDto(double Latitude, double Longitude);

/// <summary>Driver summary embedded in a ride response.</summary>
public sealed record RideDriverDto(Guid Id, string Name, double? Latitude, double? Longitude);

/// <summary>Full ride view returned by GET /api/rides/{id}.</summary>
public sealed record RideResponse
{
    public Guid RideId { get; init; }
    public Guid RiderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public RideDriverDto? Driver { get; init; }
    public LocationDto Pickup { get; init; } = new(0, 0);
    public LocationDto Destination { get; init; } = new(0, 0);
    public DateTime CreatedAt { get; init; }
    public DateTime? MatchedAt { get; init; }

    public static RideResponse From(Ride r) => new()
    {
        RideId = r.Id,
        RiderId = r.RiderId,
        Status = r.Status.ToString(),
        Driver = r.Driver is null
            ? null
            : new RideDriverDto(r.Driver.Id, r.Driver.Name, r.Driver.Latitude, r.Driver.Longitude),
        Pickup = new LocationDto(r.PickupLatitude, r.PickupLongitude),
        Destination = new LocationDto(r.DestinationLatitude, r.DestinationLongitude),
        CreatedAt = r.CreatedAt,
        MatchedAt = r.MatchedAt
    };
}
