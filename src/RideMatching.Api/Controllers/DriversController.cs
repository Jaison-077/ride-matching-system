using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RideMatching.Api.Domain;
using RideMatching.Api.DTOs;
using RideMatching.Api.Security;
using RideMatching.Api.Services;

namespace RideMatching.Api.Controllers;

/// <summary>
/// Driver management and presence endpoints. Driver operations require the Driver
/// role and act only on the authenticated driver's own record — the route id must
/// match the token subject, never a client-chosen id.
/// </summary>
[ApiController]
[Route("api/drivers")]
[Produces("application/json")]
public sealed class DriversController : ControllerBase
{
    private readonly DriverService _drivers;

    public DriversController(DriverService drivers) => _drivers = drivers;

    /// <summary>
    /// Register a new driver. Anonymous (registration): the caller then obtains a
    /// Driver token for the returned id via /api/auth/token/driver.
    /// </summary>
    /// <response code="201">Driver created.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("driver-create")]
    [ProducesResponseType(typeof(DriverResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DriverResponse>> Create(
        [FromBody] CreateDriverRequest request, CancellationToken ct)
    {
        var driver = await _drivers.CreateAsync(request.Name, ct);
        var response = DriverResponse.From(driver);
        return CreatedAtAction(nameof(Get), new { driverId = driver.Id }, response);
    }

    /// <summary>Get the authenticated driver's own state.</summary>
    /// <response code="200">Driver found.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="403">Requested a driver other than yourself.</response>
    /// <response code="404">Driver not found.</response>
    [HttpGet("{driverId:guid}")]
    [Authorize(Policy = Policies.DriverOnly)]
    [ProducesResponseType(typeof(DriverResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DriverResponse>> Get(Guid driverId, CancellationToken ct)
    {
        EnsureSelf(driverId);
        var driver = await _drivers.FindAsync(driverId, ct);
        if (driver is null)
        {
            throw new DriverNotFoundException(driverId);
        }

        return Ok(DriverResponse.From(driver));
    }

    /// <summary>Bring the authenticated driver online.</summary>
    /// <response code="200">Driver is now Available.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="403">Requested a driver other than yourself.</response>
    /// <response code="404">Driver not found.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpPost("{driverId:guid}/online")]
    [Authorize(Policy = Policies.DriverOnly)]
    [EnableRateLimiting("driver-lifecycle")]
    [ProducesResponseType(typeof(DriverResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DriverResponse>> GoOnline(Guid driverId, CancellationToken ct)
    {
        EnsureSelf(driverId);
        var driver = await _drivers.GoOnlineAsync(driverId, ct);
        return Ok(DriverResponse.From(driver));
    }

    /// <summary>Take the authenticated driver offline.</summary>
    /// <response code="200">Driver is now Offline.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="403">Requested a driver other than yourself.</response>
    /// <response code="404">Driver not found.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpPost("{driverId:guid}/offline")]
    [Authorize(Policy = Policies.DriverOnly)]
    [EnableRateLimiting("driver-lifecycle")]
    [ProducesResponseType(typeof(DriverResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DriverResponse>> GoOffline(Guid driverId, CancellationToken ct)
    {
        EnsureSelf(driverId);
        var driver = await _drivers.GoOfflineAsync(driverId, ct);
        return Ok(DriverResponse.From(driver));
    }

    /// <summary>Report the authenticated driver's current location.</summary>
    /// <response code="200">Location updated.</response>
    /// <response code="400">Invalid coordinates.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="403">Requested a driver other than yourself.</response>
    /// <response code="404">Driver not found.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpPost("{driverId:guid}/location")]
    [Authorize(Policy = Policies.DriverOnly)]
    [EnableRateLimiting("driver-location")]
    [ProducesResponseType(typeof(DriverResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DriverResponse>> UpdateLocation(
        Guid driverId, [FromBody] UpdateLocationRequest request, CancellationToken ct)
    {
        EnsureSelf(driverId);
        var driver = await _drivers.UpdateLocationAsync(driverId, request.Latitude, request.Longitude, ct);
        return Ok(DriverResponse.From(driver));
    }

    /// <summary>
    /// Ownership guard: the route driver id must equal the authenticated subject id.
    /// Prevents one driver from operating on another driver's record.
    /// </summary>
    private void EnsureSelf(Guid driverId)
    {
        if (User.GetSubjectId() != driverId)
        {
            throw new ForbiddenException("You may only act on your own driver record.");
        }
    }
}
