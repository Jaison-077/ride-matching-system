using System.Threading.Channels;

namespace RideMatching.Api.Background;

/// <summary>
/// <see cref="Channel{T}"/>-backed implementation of <see cref="IRideMatchingQueue"/>.
/// Registered as a singleton so producers (the API) and the consumer
/// (<see cref="RideMatchingWorker"/>) share one unbounded channel.
/// </summary>
public sealed class RideMatchingQueue : IRideMatchingQueue
{
    private readonly Channel<Guid> _channel =
        Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(Guid rideId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(rideId, ct);

    public ValueTask<Guid> DequeueAsync(CancellationToken ct) =>
        _channel.Reader.ReadAsync(ct);
}
