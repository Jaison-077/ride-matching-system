using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RideMatching.Api.Configuration;
using RideMatching.Api.Data;
using RideMatching.Api.Domain.Entities;
using RideMatching.Api.Domain.Enums;
using RideMatching.Api.Services;

namespace RideMatching.Tests;

public class MatchingServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly FakeRedisLocationService _redis = new();
    private readonly RecordingRideNotifier _notifier = new();

    private static readonly IOptions<MatchingOptions> Options =
        Microsoft.Extensions.Options.Options.Create(new MatchingOptions
        {
            SearchRadiusKm = 5,
            MaxCandidates = 10,
            PresenceTtlSeconds = 30
        });

    private MatchingService NewService(AppDbContext ctx) =>
        new(ctx, _redis, _notifier, Options, NullLogger<MatchingService>.Instance);

    private async Task<Driver> AddDriverAsync(AppDbContext ctx, DriverStatus status)
    {
        var driver = new Driver
        {
            Id = Guid.NewGuid(),
            Name = $"Driver {Guid.NewGuid():N}",
            Status = status,
            Latitude = 28.6139,
            Longitude = 77.2090,
            CreatedAt = DateTime.UtcNow
        };
        ctx.Drivers.Add(driver);
        await ctx.SaveChangesAsync();
        return driver;
    }

    private async Task<Ride> AddMatchingRideAsync(AppDbContext ctx)
    {
        var ride = new Ride
        {
            Id = Guid.NewGuid(),
            RiderId = Guid.NewGuid(),
            PickupLatitude = 28.6139,
            PickupLongitude = 77.2090,
            DestinationLatitude = 28.5355,
            DestinationLongitude = 77.3910,
            Status = RideStatus.Matching,
            CreatedAt = DateTime.UtcNow
        };
        ctx.Rides.Add(ride);
        await ctx.SaveChangesAsync();
        return ride;
    }

    [Fact]
    public async Task No_nearby_drivers_marks_ride_NoDriverAvailable()
    {
        await using var ctx = _db.CreateContext();
        var ride = await AddMatchingRideAsync(ctx);
        // No candidates configured in _redis.

        await NewService(ctx).MatchAsync(ride.Id, CancellationToken.None);

        var updated = await _db.CreateContext().Rides.FindAsync(ride.Id);
        updated!.Status.Should().Be(RideStatus.NoDriverAvailable);
        _notifier.NoDriverAvailable.Should().Contain(ride.Id);
    }

    [Fact]
    public async Task Successful_match_assigns_nearest_driver_and_records_assignment()
    {
        await using var ctx = _db.CreateContext();
        var near = await AddDriverAsync(ctx, DriverStatus.Available);
        var far = await AddDriverAsync(ctx, DriverStatus.Available);
        _redis.MarkPresent(near.Id, far.Id);
        // Nearest-first ordering: 'near' precedes 'far'.
        _redis.Candidates.Add(new NearbyDriver(near.Id, 0.5));
        _redis.Candidates.Add(new NearbyDriver(far.Id, 3.0));

        var ride = await AddMatchingRideAsync(ctx);

        await NewService(ctx).MatchAsync(ride.Id, CancellationToken.None);

        await using var verify = _db.CreateContext();
        var updated = await verify.Rides.FindAsync(ride.Id);
        updated!.Status.Should().Be(RideStatus.Matched);
        updated.DriverId.Should().Be(near.Id);
        updated.MatchedAt.Should().NotBeNull();

        var driver = await verify.Drivers.FindAsync(near.Id);
        driver!.Status.Should().Be(DriverStatus.Busy);

        verify.RideAssignments.Should()
            .ContainSingle(a => a.RideId == ride.Id && a.DriverId == near.Id && a.Success);

        _notifier.RideMatched.Should().Contain(ride.Id);
    }

    [Fact]
    public async Task Unavailable_nearest_driver_is_skipped_and_next_candidate_used()
    {
        await using var ctx = _db.CreateContext();
        var busy = await AddDriverAsync(ctx, DriverStatus.Busy);        // nearest, but not claimable
        var available = await AddDriverAsync(ctx, DriverStatus.Available);
        _redis.MarkPresent(busy.Id, available.Id);
        _redis.Candidates.Add(new NearbyDriver(busy.Id, 0.2));
        _redis.Candidates.Add(new NearbyDriver(available.Id, 1.0));

        var ride = await AddMatchingRideAsync(ctx);

        await NewService(ctx).MatchAsync(ride.Id, CancellationToken.None);

        await using var verify = _db.CreateContext();
        var updated = await verify.Rides.FindAsync(ride.Id);
        updated!.Status.Should().Be(RideStatus.Matched);
        updated.DriverId.Should().Be(available.Id);

        // A failed attempt against the busy driver should be recorded (fallback).
        verify.RideAssignments.Should()
            .Contain(a => a.RideId == ride.Id && a.DriverId == busy.Id && !a.Success);
        verify.RideAssignments.Should()
            .Contain(a => a.RideId == ride.Id && a.DriverId == available.Id && a.Success);
    }

    [Fact]
    public async Task Candidate_without_presence_is_skipped()
    {
        await using var ctx = _db.CreateContext();
        var noPresence = await AddDriverAsync(ctx, DriverStatus.Available);
        var present = await AddDriverAsync(ctx, DriverStatus.Available);
        _redis.MarkPresent(present.Id); // noPresence intentionally not marked
        _redis.Candidates.Add(new NearbyDriver(noPresence.Id, 0.1));
        _redis.Candidates.Add(new NearbyDriver(present.Id, 0.9));

        var ride = await AddMatchingRideAsync(ctx);

        await NewService(ctx).MatchAsync(ride.Id, CancellationToken.None);

        await using var verify = _db.CreateContext();
        var updated = await verify.Rides.FindAsync(ride.Id);
        updated!.DriverId.Should().Be(present.Id);
    }

    [Fact]
    public async Task Ride_not_in_Matching_state_is_left_untouched()
    {
        await using var ctx = _db.CreateContext();
        var driver = await AddDriverAsync(ctx, DriverStatus.Available);
        _redis.MarkPresent(driver.Id);
        _redis.Candidates.Add(new NearbyDriver(driver.Id, 0.1));

        var ride = await AddMatchingRideAsync(ctx);
        ride.Status = RideStatus.Cancelled;
        await ctx.SaveChangesAsync();

        await NewService(ctx).MatchAsync(ride.Id, CancellationToken.None);

        await using var verify = _db.CreateContext();
        (await verify.Rides.FindAsync(ride.Id))!.Status.Should().Be(RideStatus.Cancelled);
        (await verify.Drivers.FindAsync(driver.Id))!.Status.Should().Be(DriverStatus.Available);
    }

    public void Dispose() => _db.Dispose();
}
