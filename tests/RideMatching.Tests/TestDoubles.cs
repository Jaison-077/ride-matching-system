using System.Collections.Concurrent;
using RideMatching.Api.Background;
using RideMatching.Api.Hubs;
using RideMatching.Api.Services;

namespace RideMatching.Tests;

/// <summary>Records enqueued ride ids without a running background worker.</summary>
public sealed class FakeRideMatchingQueue : IRideMatchingQueue
{
    public ConcurrentQueue<Guid> Enqueued { get; } = new();

    public ValueTask EnqueueAsync(Guid rideId, CancellationToken ct = default)
    {
        Enqueued.Enqueue(rideId);
        return ValueTask.CompletedTask;
    }

    public ValueTask<Guid> DequeueAsync(CancellationToken ct) =>
        Enqueued.TryDequeue(out var id) ? ValueTask.FromResult(id) : ValueTask.FromResult(Guid.Empty);
}

/// <summary>
/// In-memory stand-in for Redis. Lets tests control which drivers are "nearby"
/// and present without needing a running Redis instance.
/// </summary>
public sealed class FakeRedisLocationService : IRedisLocationService
{
    private readonly ConcurrentDictionary<Guid, (double Lat, double Lng)> _locations = new();
    private readonly ConcurrentDictionary<Guid, bool> _presence = new();

    /// <summary>Ordered candidate list returned by SearchNearbyAsync (nearest-first).</summary>
    public List<NearbyDriver> Candidates { get; } = new();

    public Task UpsertDriverLocationAsync(Guid driverId, double latitude, double longitude, CancellationToken ct = default)
    {
        _locations[driverId] = (latitude, longitude);
        return Task.CompletedTask;
    }

    public Task RemoveDriverLocationAsync(Guid driverId, CancellationToken ct = default)
    {
        _locations.TryRemove(driverId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NearbyDriver>> SearchNearbyAsync(double latitude, double longitude, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<NearbyDriver>>(Candidates.ToList());

    public Task SetPresenceAsync(Guid driverId, CancellationToken ct = default)
    {
        _presence[driverId] = true;
        return Task.CompletedTask;
    }

    public Task RefreshPresenceAsync(Guid driverId, CancellationToken ct = default) =>
        SetPresenceAsync(driverId, ct);

    public Task<bool> IsPresentAsync(Guid driverId, CancellationToken ct = default) =>
        Task.FromResult(_presence.TryGetValue(driverId, out var present) && present);

    public Task RemovePresenceAsync(Guid driverId, CancellationToken ct = default)
    {
        _presence.TryRemove(driverId, out _);
        return Task.CompletedTask;
    }

    public void MarkPresent(params Guid[] driverIds)
    {
        foreach (var id in driverIds)
        {
            _presence[id] = true;
        }
    }
}

/// <summary>Records notifier calls so tests can assert real-time events fired.</summary>
public sealed class RecordingRideNotifier : IRideNotifier
{
    public ConcurrentBag<Guid> RideMatched { get; } = new();
    public ConcurrentBag<Guid> NoDriverAvailable { get; } = new();
    public ConcurrentBag<(Guid RideId, string Status)> StatusChanges { get; } = new();
    public ConcurrentBag<Guid> DriverAssigned { get; } = new();

    public Task RideMatchedAsync(Guid rideId, Guid riderId, Guid driverId, string driverName, CancellationToken ct = default)
    {
        RideMatched.Add(rideId);
        return Task.CompletedTask;
    }

    public Task RideStatusChangedAsync(Guid rideId, Guid riderId, string status, CancellationToken ct = default)
    {
        StatusChanges.Add((rideId, status));
        return Task.CompletedTask;
    }

    public Task DriverAssignedAsync(Guid driverId, Guid rideId, CancellationToken ct = default)
    {
        DriverAssigned.Add(driverId);
        return Task.CompletedTask;
    }

    public Task DriverLocationUpdatedAsync(Guid driverId, double latitude, double longitude, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task NoDriverAvailableAsync(Guid rideId, Guid riderId, CancellationToken ct = default)
    {
        NoDriverAvailable.Add(rideId);
        return Task.CompletedTask;
    }
}
