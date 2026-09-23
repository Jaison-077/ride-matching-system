using Microsoft.AspNetCore.Mvc;
using RideMatching.Api.Domain;
using RideMatching.Api.Security;

namespace RideMatching.Api.Controllers;

/// <summary>
/// Lightweight development token endpoint. In a real deployment this is replaced
/// by a proper identity provider / login flow; here it issues a signed JWT for a
/// given role and subject id so the protected APIs can be exercised.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly JwtTokenService _tokens;

    public AuthController(JwtTokenService tokens) => _tokens = tokens;

    /// <summary>Issue a token for a rider. If no riderId is supplied a new one is generated.</summary>
    [HttpPost("token/rider")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    public ActionResult<TokenResponse> RiderToken([FromBody] TokenRequest? request)
    {
        var id = request?.SubjectId ?? Guid.NewGuid();
        return Ok(new TokenResponse(_tokens.CreateToken(id, Roles.Rider), id, Roles.Rider));
    }

    /// <summary>Issue a token for a driver. The driverId must be supplied (the driver's own id).</summary>
    [HttpPost("token/driver")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<TokenResponse> DriverToken([FromBody] TokenRequest request)
    {
        if (request.SubjectId is not Guid id || id == Guid.Empty)
        {
            throw new ValidationException("A driver token requires the driver's SubjectId.");
        }

        return Ok(new TokenResponse(_tokens.CreateToken(id, Roles.Driver), id, Roles.Driver));
    }
}

/// <summary>Optional subject id to embed in the token.</summary>
public sealed record TokenRequest(Guid? SubjectId);

/// <summary>Issued token plus the resolved subject and role.</summary>
public sealed record TokenResponse(string AccessToken, Guid SubjectId, string Role);
