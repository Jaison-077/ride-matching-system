namespace RideMatching.Api.Background;

/// <summary>
/// In-process queue of ride ids awaiting background matching.
/// For a single-instance assessment deployment an in-process channel is used
/// intentionally; in production this would be a durable distributed broker.
/// </summary>
public interface IRideMatchingQueue
{
    ValueTask EnqueueAsync(Guid rideId, CancellationToken ct = default);

    ValueTask<Guid> DequeueAsync(CancellationToken ct);
}
