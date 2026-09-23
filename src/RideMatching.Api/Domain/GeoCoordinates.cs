namespace RideMatching.Api.Domain;

/// <summary>
/// Coordinate validation and distance helpers. Kept in the domain layer so both
/// the API validation path and the matching logic share one implementation.
/// </summary>
public static class GeoCoordinates
{
    public const double MinLatitude = -90d;
    public const double MaxLatitude = 90d;
    public const double MinLongitude = -180d;
    public const double MaxLongitude = 180d;

    public static bool IsValidLatitude(double latitude) =>
        latitude is >= MinLatitude and <= MaxLatitude && !double.IsNaN(latitude);

    public static bool IsValidLongitude(double longitude) =>
        longitude is >= MinLongitude and <= MaxLongitude && !double.IsNaN(longitude);

    public static bool IsValid(double latitude, double longitude) =>
        IsValidLatitude(latitude) && IsValidLongitude(longitude);
}
