using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RideMatching.Api.Configuration;
using RideMatching.Api.Data;
using RideMatching.Api.Domain.Entities;
using RideMatching.Api.Domain.Enums;
using RideMatching.Api.Services;

namespace RideMatching.Tests;

/// <summary>
/// Mandatory concurrency proof.
///
/// Two rides discover the SAME single available driver (as would happen when both
/// see it through Redis) and are matched concurrently, each with its own
/// <see cref="AppDbContext"/> and its own <see cref="MatchingService"/> — the same
/// isolation two real web requests / worker invocations would have.
///
/// The assignment relies on an atomic conditional UPDATE (Available -&gt; Busy) whose
/// affected-row count decides the winner. This test asserts that exactly one ride
/// wins, the driver ends Busy, and the loser does not take that driver.
/// </summary>
public class ConcurrencyTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly FakeRedisLocationService _redis = new();
    private readonly RecordingRideNotifier _notifier = new();

    private static readonly IOptions<MatchingOptions> Options =
        Microsoft.Extensions.Options.Options.Create(new MatchingOptions());

    private MatchingService NewService(AppDbContext ctx) =>
        new(ctx, _redis, _notifier, Options, NullLogger<MatchingService>.Instance);

    [Fact]
    public async Task Two_rides_competing_for_one_driver_only_one_wins()
    {
        Guid driverId;
        Guid rideAId;
        Guid rideBId;

        await using (var seed = _db.CreateContext())
        {
            var driver = new Driver
            {
                Id = Guid.NewGuid(),
                Name = "Sole Driver",
                Status = DriverStatus.Available,
                Latitude = 28.6139,
                Longitude = 77.2090,
                LastSeenAt = DateTime.UtcNow, // fresh so it passes the freshness filter
                CreatedAt = DateTime.UtcNow
            };
            var rideA = NewMatchingRide();
            var rideB = NewMatchingRide();
            seed.Drivers.Add(driver);
            seed.Rides.AddRange(rideA, rideB);
            await seed.SaveChangesAsync();

            driverId = driver.Id;
            rideAId = rideA.Id;
            rideBId = rideB.Id;
        }

        // Both rides see the same single candidate.
        _redis.MarkPresent(driverId);
        _redis.Candidates.Add(new NearbyDriver(driverId, 0.3));

        // Run both matching operations concurrently, each with an independent context.
        async Task RunAsync(Guid rideId)
        {
            await using var ctx = _db.CreateContext();
            await NewService(ctx).MatchAsync(rideId, CancellationToken.None);
        }

        var barrier = new TaskCompletionSource();
        var taskA = Task.Run(async () => { await barrier.Task; await RunAsync(rideAId); });
        var taskB = Task.Run(async () => { await barrier.Task; await RunAsync(rideBId); });
        barrier.SetResult(); // release both as simultaneously as possible
        await Task.WhenAll(taskA, taskB);

        await using var verify = _db.CreateContext();
        var rideA2 = await verify.Rides.FindAsync(rideAId);
        var rideB2 = await verify.Rides.FindAsync(rideBId);
        var driver2 = await verify.Drivers.FindAsync(driverId);

        // Exactly one ride is Matched to the driver.
        var matchedRides = new[] { rideA2!, rideB2! }.Where(r => r.Status == RideStatus.Matched).ToList();
        matchedRides.Should().HaveCount(1, "only one ride may claim the single driver");
        matchedRides[0].DriverId.Should().Be(driverId);

        // The other ride did NOT get that driver.
        var otherRide = new[] { rideA2!, rideB2! }.Single(r => r.Status != RideStatus.Matched);
        otherRide.DriverId.Should().NotBe(driverId);
        otherRide.Status.Should().Be(RideStatus.NoDriverAvailable);

        // Driver ends Busy.
        driver2!.Status.Should().Be(DriverStatus.Busy);

        // Exactly one successful assignment row exists for this driver.
        var successes = await verify.RideAssignments
            .Where(a => a.DriverId == driverId && a.Success)
            .ToListAsync();
        successes.Should().HaveCount(1);
    }

    private static Ride NewMatchingRide() => new()
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

    public void Dispose() => _db.Dispose();
}
