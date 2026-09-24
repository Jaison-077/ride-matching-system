using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RideMatching.Api.Domain;
using RideMatching.Api.DTOs;
using RideMatching.Api.Security;
using RideMatching.Api.Services;

namespace RideMatching.Api.Controllers;

/// <summary>
/// Ride request and status endpoints. Requires the Rider role. The rider identity
/// is always taken from the authenticated token, never from the request body, and
/// a rider may only read their own rides.
/// </summary>
[ApiController]
[Route("api/rides")]
[Produces("application/json")]
[Authorize(Policy = Policies.RiderOnly)]
public sealed class RidesController : ControllerBase
{
    private readonly RideService _rides;

    public RidesController(RideService rides) => _rides = rides;

    /// <summary>
    /// Create a ride for the authenticated rider. Returns immediately with status
    /// "Matching"; matching runs asynchronously. Supply an optional
    /// <c>Idempotency-Key</c> header (scoped to the authenticated rider) to safely retry.
    /// </summary>
    /// <response code="201">Ride accepted for matching.</response>
    /// <response code="200">Idempotent replay of a previously created ride.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpPost]
    [EnableRateLimiting("ride-create")]
    [ProducesResponseType(typeof(CreateRideResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CreateRideResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreateRideResponse>> Create(
        [FromBody] CreateRideRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        // Rider identity comes from the token, not the request body.
        var riderId = User.GetSubjectId();

        var result = await _rides.CreateAsync(
            riderId,
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

    /// <summary>Get one of the authenticated rider's own rides.</summary>
    /// <response code="200">Ride found.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="403">Ride belongs to another rider.</response>
    /// <response code="404">Ride not found.</response>
    [HttpGet("{rideId:guid}")]
    [ProducesResponseType(typeof(RideResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RideResponse>> Get(Guid rideId, CancellationToken ct)
    {
        var ride = await _rides.GetAsync(rideId, ct);
        if (ride is null)
        {
            throw new RideNotFoundException(rideId);
        }

        // Ownership: a rider may only view their own rides.
        if (ride.RiderId != User.GetSubjectId())
        {
            throw new ForbiddenException("You may only view your own rides.");
        }

        return Ok(RideResponse.From(ride));
    }
}
