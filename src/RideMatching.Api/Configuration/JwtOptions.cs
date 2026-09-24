namespace RideMatching.Api.Configuration;

/// <summary>
/// JWT signing/validation settings, bound from the "Jwt" configuration section.
/// The signing key must be supplied via configuration/environment/secret store —
/// there is no hard-coded production default.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "ride-matching";
    public string Audience { get; set; } = "ride-matching";

    /// <summary>Symmetric signing key (HMAC-SHA256). Required; must be >= 32 bytes.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Access-token lifetime in minutes.</summary>
    public int AccessTokenMinutes { get; set; } = 60;
}
