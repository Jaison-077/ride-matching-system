using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RideMatching.Api.Configuration;
using RideMatching.Api.Data;
using RideMatching.Api.Domain.Entities;
using RideMatching.Api.Domain.Enums;
using RideMatching.Api.Hubs;

namespace RideMatching.Api.Services;

/// <summary>
/// Core matching algorithm.
///
/// Redis discovers nearby candidate drivers; SQL Server is the sole authority
/// for whether a driver can actually be claimed. The claim is an atomic,
/// conditional UPDATE (Available -&gt; Busy) whose affected-row count decides the
/// winner, preventing two concurrent rides from taking the same driver.
/// </summary>
public sealed class MatchingService
{
    private readonly AppDbContext _db;
    private readonly IRedisLocationService _redis;
    private readonly IRideNotifier _notifier;
    private readonly MatchingOptions _options;
    private readonly ILogger<MatchingService> _logger;

    public MatchingService(
        AppDbContext db,
        IRedisLocationService redis,
        IRideNotifier notifier,
        IOptions<MatchingOptions> options,
        ILogger<MatchingService> logger)
    {
        _db = db;
        _redis = redis;
        _notifier = notifier;
        _options = options.Value;
        _logger = logger;
    }

    public async Task MatchAsync(Guid rideId, CancellationToken ct)
    {
        var ride = await _db.Rides.FirstOrDefaultAsync(r => r.Id == rideId, ct);
        if (ride is null)
        {
            _logger.LogWarning("Matching skipped: ride {RideId} not found.", rideId);
            return;
        }

        // Only rides in Matching are eligible (e.g. a cancelled ride stops safely).
        if (ride.Status != RideStatus.Matching)
        {
            _logger.LogInformation(
                "Matching skipped: ride {RideId} is in state {Status}.", rideId, ride.Status);
            return;
        }

        _logger.LogInformation("MatchingStarted {RideId}", rideId);

        var candidates = await _redis.SearchNearbyAsync(ride.PickupLatitude, ride.PickupLongitude, ct);
        _logger.LogInformation(
            "CandidateDriversFound {RideId} CandidateCount {CandidateCount}", rideId, candidates.Count);

        if (candidates.Count == 0)
        {
            await MarkNoDriverAsync(ride, ct);
            return;
        }

        var attempt = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            attempt++;

            // Presence gate (fast, best-effort). SQL still guards the real claim.
            if (!await _redis.IsPresentAsync(candidate.DriverId, ct))
            {
                _logger.LogInformation(
                    "Candidate {DriverId} for ride {RideId} has no presence; skipping.",
                    candidate.DriverId, rideId);
                continue;
            }

            var claimed = await TryClaimAsync(ride, candidate.DriverId, attempt, candidate.DistanceKm, ct);
            if (claimed is not null)
            {
                // The claim is committed. Post-commit side effects (Redis cleanup +
                // notifications) must run even if a shutdown was requested, so they
                // are not tied to the matching CancellationToken.
                await NotifyMatchedAsync(claimed);
                return;
            }
        }

        await MarkNoDriverAsync(ride, ct);
    }

    /// <summary>
    /// Attempts to claim <paramref name="driverId"/> for <paramref name="ride"/> inside a short
    /// SQL transaction. Returns the updated ride on success, otherwise null.
    /// No Redis or SignalR calls happen while the transaction is open.
    /// </summary>
    private async Task<Ride?> TryClaimAsync(
        Ride ride, Guid driverId, int attempt, double distanceKm, CancellationToken ct)
    {
        // Freshness cutoff: a driver is only claimable if their last heartbeat is
        // recent enough. Stale drivers (no recent LastSeenAt) are excluded by the
        // same atomic UPDATE, so freshness is enforced race-free alongside the claim.
        var freshnessCutoff = DateTime.UtcNow.AddSeconds(-_options.DriverFreshnessSeconds);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // Atomic conditional claim: Available AND fresh -> Busy. The affected row
            // count is the source of truth for who wins the race.
            var rows = await _db.Drivers
                .Where(d => d.Id == driverId
                            && d.Status == DriverStatus.Available
                            && d.LastSeenAt != null
                            && d.LastSeenAt >= freshnessCutoff)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(d => d.Status, DriverStatus.Busy), ct);

            if (rows == 0)
            {
                // Someone else took the driver, or it went offline/busy.
                _db.RideAssignments.Add(new RideAssignment
                {
                    Id = Guid.NewGuid(),
                    RideId = ride.Id,
                    DriverId = driverId,
                    AttemptNumber = attempt,
                    AssignedAt = DateTime.UtcNow,
                    Success = false
                });
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "DriverClaimFailed {RideId} {DriverId} AttemptNumber {AttemptNumber}",
                    ride.Id, driverId, attempt);
                return null;
            }

            // Reload the ride from the database (not the tracked cache) so the guard
            // genuinely observes any concurrent state change, e.g. a cancellation.
            var tracked = await _db.Rides.FirstAsync(r => r.Id == ride.Id, ct);
            await _db.Entry(tracked).ReloadAsync(ct);
            if (tracked.Status != RideStatus.Matching)
            {
                // Ride is no longer matchable; release the driver we just claimed.
                await _db.Drivers
                    .Where(d => d.Id == driverId && d.Status == DriverStatus.Busy)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(d => d.Status, DriverStatus.Available), ct);
                await tx.CommitAsync(ct);
                _logger.LogInformation(
                    "Ride {RideId} left Matching mid-claim; released driver {DriverId}.",
                    ride.Id, driverId);
                return null;
            }

            tracked.Status = RideStatus.Matched;
            tracked.DriverId = driverId;
            tracked.MatchedAt = DateTime.UtcNow;

            _db.RideAssignments.Add(new RideAssignment
            {
                Id = Guid.NewGuid(),
                RideId = tracked.Id,
                DriverId = driverId,
                AttemptNumber = attempt,
                AssignedAt = DateTime.UtcNow,
                Success = true
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "DriverClaimSucceeded {RideId} {DriverId} AttemptNumber {AttemptNumber} Distance {Distance}",
                tracked.Id, driverId, attempt, distanceKm);
            _logger.LogInformation("RideMatched {RideId} {DriverId}", tracked.Id, driverId);

            return await _db.Rides.AsNoTracking()
                .Include(r => r.Driver)
                .FirstAsync(r => r.Id == tracked.Id, ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Assignment transaction failed for ride {RideId} driver {DriverId}",
                ride.Id, driverId);
            return null;
        }
    }

    private async Task NotifyMatchedAsync(Ride ride)
    {
        // Runs after the transaction has committed; uses CancellationToken.None so a
        // graceful shutdown still flushes Redis cleanup and client notifications.
        var ct = CancellationToken.None;
        if (ride.DriverId is Guid driverId)
        {
            await _redis.RemoveDriverLocationAsync(driverId, ct);
            await _notifier.RideMatchedAsync(
                ride.Id, ride.RiderId, driverId, ride.Driver?.Name ?? string.Empty, ct);
            await _notifier.DriverAssignedAsync(driverId, ride.Id, ct);
        }

        await _notifier.RideStatusChangedAsync(ride.Id, ride.RiderId, ride.Status.ToString(), ct);
    }

    private async Task MarkNoDriverAsync(Ride ride, CancellationToken ct)
    {
        var tracked = await _db.Rides.FirstAsync(r => r.Id == ride.Id, ct);
        if (tracked.Status != RideStatus.Matching)
        {
            return;
        }

        tracked.Status = RideStatus.NoDriverAvailable;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("NoDriverAvailable {RideId}", ride.Id);

        // Notifications are post-commit; not tied to the matching token.
        await _notifier.NoDriverAvailableAsync(tracked.Id, tracked.RiderId, CancellationToken.None);
        await _notifier.RideStatusChangedAsync(
            tracked.Id, tracked.RiderId, tracked.Status.ToString(), CancellationToken.None);
    }
}
