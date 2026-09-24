namespace RideMatching.Api.Security;

/// <summary>Role names used across authorization policies and token issuance.</summary>
public static class Roles
{
    public const string Rider = "Rider";
    public const string Driver = "Driver";
}

/// <summary>Authorization policy names.</summary>
public static class Policies
{
    public const string RiderOnly = "RiderOnly";
    public const string DriverOnly = "DriverOnly";
}
