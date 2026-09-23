using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RideMatching.Api.DTOs;
using RideMatching.Api.Services;

namespace RideMatching.Api.Controllers;

/// <summary>Driver management and presence endpoints.</summary>
[ApiController]
[Route("api/drivers")]
[Produces("application/json")]
public sealed class DriversController : ControllerBase
{
    private readonly DriverService _drivers;

    public DriversController(DriverService drivers) => _drivers = drivers;

    /// <summary>Create a new driver. New drivers start Offline.</summary>
    /// <response code="201">Driver created.</response>
    /// <response code="400">Validation failed.</response>
    [HttpPost]
    [ProducesResponseType(typeof(DriverResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DriverResponse>> Create(
        [FromBody] CreateDriverRequest request, CancellationToken ct)
    {
        var driver = await _drivers.CreateAsync(request.Name, ct);
        var response = DriverResponse.From(driver);
        return CreatedAtAction(nameof(Get), new { driverId = driver.Id }, response);
    }

    /// <summary>Get a driver's current state.</summary>
    /// <response code="200">Driver found.</response>
    /// <response code="404">Driver not found.</response>
    [HttpGet("{driverId:guid}")]
    [ProducesResponseType(typeof(DriverResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DriverResponse>> Get(Guid driverId, CancellationToken ct)
    {
        var driver = await _drivers.FindAsync(driverId, ct);
        if (driver is null)
        {
            throw new Domain.DriverNotFoundException(driverId);
        }

        return Ok(DriverResponse.From(driver));
    }

    /// <summary>Bring a driver online so they become eligible for matching.</summary>
    /// <response code="200">Driver is now Available.</response>
    /// <response code="404">Driver not found.</response>
    [HttpPost("{driverId:guid}/online")]
    [ProducesResponseType(typeof(DriverResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DriverResponse>> GoOnline(Guid driverId, CancellationToken ct)
    {
        var driver = await _drivers.GoOnlineAsync(driverId, ct);
        return Ok(DriverResponse.From(driver));
    }

    /// <summary>Take a driver offline and remove them from Redis discovery.</summary>
    /// <response code="200">Driver is now Offline.</response>
    /// <response code="404">Driver not found.</response>
    [HttpPost("{driverId:guid}/offline")]
    [ProducesResponseType(typeof(DriverResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DriverResponse>> GoOffline(Guid driverId, CancellationToken ct)
    {
        var driver = await _drivers.GoOfflineAsync(driverId, ct);
        return Ok(DriverResponse.From(driver));
    }

    /// <summary>Report a driver's current location (updates SQL Server and Redis GEO).</summary>
    /// <response code="200">Location updated.</response>
    /// <response code="400">Invalid coordinates.</response>
    /// <response code="404">Driver not found.</response>
    [HttpPost("{driverId:guid}/location")]
    [EnableRateLimiting("driver-location")]
    [ProducesResponseType(typeof(DriverResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DriverResponse>> UpdateLocation(
        Guid driverId, [FromBody] UpdateLocationRequest request, CancellationToken ct)
    {
        var driver = await _drivers.UpdateLocationAsync(driverId, request.Latitude, request.Longitude, ct);
        return Ok(DriverResponse.From(driver));
    }
}
