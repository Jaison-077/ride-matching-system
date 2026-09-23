using Microsoft.AspNetCore.Mvc;
using RideMatching.Api.Domain;
using RideMatching.Api.DTOs;
using RideMatching.Api.Services;

namespace RideMatching.Api.Controllers;

/// <summary>Ride request and status endpoints.</summary>
[ApiController]
[Route("api/rides")]
[Produces("application/json")]
public sealed class RidesController : ControllerBase
{
    private readonly RideService _rides;

    public RidesController(RideService rides) => _rides = rides;

    /// <summary>
    /// Create a ride. Returns immediately with status "Matching"; the background
    /// worker performs the actual driver matching asynchronously.
    /// Supply an optional <c>Idempotency-Key</c> header to safely retry.
    /// </summary>
    /// <response code="201">Ride accepted for matching.</response>
    /// <response code="200">Idempotent replay of a previously created ride.</response>
    /// <response code="400">Validation failed.</response>
    [HttpPost]
    [ProducesResponseType(typeof(CreateRideResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CreateRideResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreateRideResponse>> Create(
        [FromBody] CreateRideRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var result = await _rides.CreateAsync(
            request.RiderId,
            request.PickupLatitude,
            request.PickupLongitude,
            request.DestinationLatitude,
            request.DestinationLongitude,
            idempotencyKey,
            ct);

        var response = new CreateRideResponse
        {
            RideId = result.Ride.Id,
            Status = result.Ride.Status.ToString()
        };

        if (result.WasExisting)
        {
            return Ok(response);
        }

        return CreatedAtAction(nameof(Get), new { rideId = result.Ride.Id }, response);
    }

    /// <summary>Get the current state of a ride, including the assigned driver if matched.</summary>
    /// <response code="200">Ride found.</response>
    /// <response code="404">Ride not found.</response>
    [HttpGet("{rideId:guid}")]
    [ProducesResponseType(typeof(RideResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RideResponse>> Get(Guid rideId, CancellationToken ct)
    {
        var ride = await _rides.GetAsync(rideId, ct);
        if (ride is null)
        {
            throw new RideNotFoundException(rideId);
        }

        return Ok(RideResponse.From(ride));
    }
}
