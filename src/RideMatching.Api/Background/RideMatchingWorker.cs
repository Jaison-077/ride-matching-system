using RideMatching.Api.Services;

namespace RideMatching.Api.Background;

/// <summary>
/// Consumes ride ids from <see cref="IRideMatchingQueue"/> and runs matching for
/// each in its own DI scope. Individual failures are logged and retried a bounded
/// number of times; one bad ride never terminates the worker.
/// </summary>
public sealed class RideMatchingWorker : BackgroundService
{
    private const int MaxRetries = 2;

    private readonly IRideMatchingQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RideMatchingWorker> _logger;

    public RideMatchingWorker(
        IRideMatchingQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<RideMatchingWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RideMatchingWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid rideId;
            try
            {
                rideId = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await ProcessAsync(rideId, stoppingToken);
        }

        _logger.LogInformation("RideMatchingWorker stopping gracefully.");
    }

    private async Task ProcessAsync(Guid rideId, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxRetries + 1; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var matcher = scope.ServiceProvider.GetRequiredService<MatchingService>();
                await matcher.MatchAsync(rideId, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt <= MaxRetries)
                {
                    _logger.LogWarning(ex,
                        "MatchingRetry {RideId} AttemptNumber {AttemptNumber}", rideId, attempt);
                    await DelayBeforeRetryAsync(attempt, ct);
                    continue;
                }

                _logger.LogError(ex,
                    "Matching permanently failed for ride {RideId} after {Attempts} attempts.",
                    rideId, attempt);
                return;
            }
        }
    }

    private static async Task DelayBeforeRetryAsync(int attempt, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), ct);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested during backoff; caller handles cancellation.
        }
    }
}
