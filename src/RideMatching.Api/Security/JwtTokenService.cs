using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RideMatching.Api.Configuration;

namespace RideMatching.Api.Security;

/// <summary>
/// Issues signed JWTs for a given subject id and role. Used by the lightweight
/// dev token endpoint (and by tests) to obtain Rider/Driver access tokens.
/// </summary>
public sealed class JwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    /// <summary>
    /// Creates a signed token whose subject is <paramref name="subjectId"/> and whose
    /// role is <paramref name="role"/>. The subject id is the authoritative driver/rider
    /// identity; controllers must use it rather than any client-supplied id.
    /// </summary>
    public string CreateToken(Guid subjectId, string role)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, subjectId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, subjectId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
