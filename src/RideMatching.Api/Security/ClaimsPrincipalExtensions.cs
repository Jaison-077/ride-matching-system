using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace RideMatching.Api.Security;

/// <summary>Helpers to read the authenticated identity from claims (never client input).</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The authenticated subject id (driver id or rider id). Reads NameIdentifier,
    /// falling back to the JWT "sub" claim. Throws if absent/invalid so callers can
    /// treat a missing identity as unauthorized rather than silently trusting input.
    /// </summary>
    public static Guid GetSubjectId(this ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return Guid.TryParse(raw, out var id)
            ? id
            : throw new UnauthorizedAccessException("Authenticated subject id is missing or invalid.");
    }

    public static bool IsInRoleSafe(this ClaimsPrincipal user, string role) => user.IsInRole(role);
}
