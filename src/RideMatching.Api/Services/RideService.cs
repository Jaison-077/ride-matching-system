using Microsoft.EntityFrameworkCore;
using RideMatching.Api.Background;
using RideMatching.Api.Data;
using RideMatching.Api.Domain;
using RideMatching.Api.Domain.Entities;
using RideMatching.Api.Domain.Enums;

namespace RideMatching.Api.Services;

/// <summary>Result of a ride creation, including whether it was a replay of an idempotent request.</summary>
public sealed record CreateRideResult(Ride Ride, bool WasExisting);

/// <summary>
/// Ride creation and retrieval. Creation persists the ride, transitions it to
/// Matching, and enqueues it for the background worker. Matching itself is never
/// performed synchronously here.
/// </summary>
public sealed class RideService
{
    private readonly AppDbContext _db;
    private readonly IRideMatchingQueue _queue;
    private readonly ILogger<RideService> _logger;

    public RideService(AppDbContext db, IRideMatchingQueue queue, ILogger<RideService> logger)
    {
        _db = db;
        _queue = queue;
        _logger = logger;
    }

    public async Task<CreateRideResult> CreateAsync(
        Guid riderId,
        double pickupLat,
        double pickupLng,
        double destLat,
        double destLng,
        string? idempotencyKey,
        CancellationToken ct)
    {
        if (riderId == Guid.Empty)
        {
            throw new ValidationException("RiderId is required.");
        }

        if (!GeoCoordinates.IsValid(pickupLat, pickupLng))
        {
            throw new ValidationException("Invalid pickup coordinates.");
        }

        if (!GeoCoordinates.IsValid(destLat, destLng))
        {
            throw new ValidationException("Invalid destination coordinates.");
        }

        // Idempotency is scoped to the authenticated rider: same rider + same key
        // returns the same ride; different riders may reuse the same key value.
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await _db.Rides.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RiderId == riderId && r.IdempotencyKey == idempotencyKey, ct);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Idempotent ride replay {RideId} for rider {RiderId} key {Key}",
                    existing.Id, riderId, idempotencyKey);
                return new CreateRideResult(existing, WasExisting: true);
            }
        }

        var ride = new Ride
        {
            Id = Guid.NewGuid(),
            RiderId = riderId,
            PickupLatitude = pickupLat,
            PickupLongitude = pickupLng,
            DestinationLatitude = destLat,
            DestinationLongitude = destLng,
            Status = RideStatus.Requested,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
            CreatedAt = DateTime.UtcNow
        };

        RideStateMachine.EnsureCanTransition(ride.Status, RideStatus.Matching);
        ride.Status = RideStatus.Matching;

        _db.Rides.Add(ride);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            // A concurrent request from the same rider with the same key won the
            // (RiderId, IdempotencyKey) unique-index race; return that ride.
            _db.Entry(ride).State = EntityState.Detached;
            var existing = await _db.Rides.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RiderId == riderId && r.IdempotencyKey == idempotencyKey, ct);
            if (existing is not null)
            {
                return new CreateRideResult(existing, WasExisting: true);
            }
            throw;
        }

        _logger.LogInformation("RideCreated {RideId} Rider {RiderId}", ride.Id, ride.RiderId);

        // The ride is already committed as Matching. Enqueue must NOT be tied to the
        // request's CancellationToken: if the client disconnects here the ride would
        // be persisted but never queued, stranding it in Matching forever.
        await _queue.EnqueueAsync(ride.Id, CancellationToken.None);
        return new CreateRideResult(ride, WasExisting: false);
    }

    public async Task<Ride?> GetAsync(Guid rideId, CancellationToken ct) =>
        await _db.Rides.AsNoTracking()
            .Include(r => r.Driver)
            .FirstOrDefaultAsync(r => r.Id == rideId, ct);
}
