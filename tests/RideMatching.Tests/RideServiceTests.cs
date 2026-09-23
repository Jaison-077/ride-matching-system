using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RideMatching.Api.Domain;
using RideMatching.Api.Domain.Enums;
using RideMatching.Api.Services;

namespace RideMatching.Tests;

public class RideServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly FakeRideMatchingQueue _queue = new();

    private RideService NewService() =>
        new(_db.CreateContext(), _queue, NullLogger<RideService>.Instance);

    [Fact]
    public async Task Create_persists_ride_as_Matching_and_enqueues_it()
    {
        var result = await NewService().CreateAsync(
            Guid.NewGuid(), 28.6139, 77.2090, 28.5355, 77.3910, idempotencyKey: null, CancellationToken.None);

        result.WasExisting.Should().BeFalse();
        result.Ride.Status.Should().Be(RideStatus.Matching);
        _queue.Enqueued.Should().ContainSingle().Which.Should().Be(result.Ride.Id);
    }

    [Fact]
    public async Task Create_with_empty_rider_is_rejected()
    {
        var act = async () => await NewService().CreateAsync(
            Guid.Empty, 28.6139, 77.2090, 28.5355, 77.3910, null, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Theory]
    [InlineData(200, 77.2090, 28.5355, 77.3910)]   // bad pickup lat
    [InlineData(28.6139, 200, 28.5355, 77.3910)]   // bad pickup lng
    [InlineData(28.6139, 77.2090, 200, 77.3910)]   // bad dest lat
    [InlineData(28.6139, 77.2090, 28.5355, 200)]   // bad dest lng
    public async Task Create_with_invalid_coordinates_is_rejected(
        double pLat, double pLng, double dLat, double dLng)
    {
        var act = async () => await NewService().CreateAsync(
            Guid.NewGuid(), pLat, pLng, dLat, dLng, null, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Repeated_idempotency_key_returns_same_ride_and_enqueues_once()
    {
        var rider = Guid.NewGuid();
        const string key = "idem-123";

        var first = await NewService().CreateAsync(rider, 28.61, 77.20, 28.53, 77.39, key, CancellationToken.None);
        var second = await NewService().CreateAsync(rider, 28.61, 77.20, 28.53, 77.39, key, CancellationToken.None);

        first.WasExisting.Should().BeFalse();
        second.WasExisting.Should().BeTrue();
        second.Ride.Id.Should().Be(first.Ride.Id);

        // Only the first creation should have enqueued work.
        _queue.Enqueued.Should().ContainSingle();
    }

    public void Dispose() => _db.Dispose();
}
