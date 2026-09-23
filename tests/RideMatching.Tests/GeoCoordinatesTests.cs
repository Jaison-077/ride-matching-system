using FluentAssertions;
using RideMatching.Api.Domain;

namespace RideMatching.Tests;

public class GeoCoordinatesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-90)]
    [InlineData(90)]
    [InlineData(28.6139)]
    public void Valid_latitude_is_accepted(double latitude) =>
        GeoCoordinates.IsValidLatitude(latitude).Should().BeTrue();

    [Theory]
    [InlineData(-90.0001)]
    [InlineData(90.0001)]
    [InlineData(1000)]
    [InlineData(double.NaN)]
    public void Invalid_latitude_is_rejected(double latitude) =>
        GeoCoordinates.IsValidLatitude(latitude).Should().BeFalse();

    [Theory]
    [InlineData(0)]
    [InlineData(-180)]
    [InlineData(180)]
    [InlineData(77.2090)]
    public void Valid_longitude_is_accepted(double longitude) =>
        GeoCoordinates.IsValidLongitude(longitude).Should().BeTrue();

    [Theory]
    [InlineData(-180.0001)]
    [InlineData(180.0001)]
    [InlineData(1000)]
    [InlineData(double.NaN)]
    public void Invalid_longitude_is_rejected(double longitude) =>
        GeoCoordinates.IsValidLongitude(longitude).Should().BeFalse();

    [Fact]
    public void IsValid_requires_both_coordinates_valid()
    {
        GeoCoordinates.IsValid(28.6139, 77.2090).Should().BeTrue();
        GeoCoordinates.IsValid(200, 77.2090).Should().BeFalse();
        GeoCoordinates.IsValid(28.6139, 200).Should().BeFalse();
    }
}
