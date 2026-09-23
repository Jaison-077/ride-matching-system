using RideMatching.Api.Domain.Enums;

namespace RideMatching.Api.Domain;

/// <summary>
/// Centralized, single source of truth for allowed ride status transitions.
/// Controllers and services must go through this rather than duplicating rules.
/// </summary>
public static class RideStateMachine
{
    private static readonly IReadOnlyDictionary<RideStatus, RideStatus[]> Allowed =
        new Dictionary<RideStatus, RideStatus[]>
        {
            [RideStatus.Requested] = new[] { RideStatus.Matching, RideStatus.Cancelled },
            [RideStatus.Matching] = new[]
            {
                RideStatus.Matched,
                RideStatus.NoDriverAvailable,
                RideStatus.Cancelled
            },
            [RideStatus.Matched] = new[] { RideStatus.Completed, RideStatus.Cancelled },
            [RideStatus.Completed] = Array.Empty<RideStatus>(),
            [RideStatus.Cancelled] = Array.Empty<RideStatus>(),
            [RideStatus.NoDriverAvailable] = Array.Empty<RideStatus>()
        };

    /// <summary>Returns true if <paramref name="to"/> is reachable from <paramref name="from"/>.</summary>
    public static bool CanTransition(RideStatus from, RideStatus to) =>
        Allowed.TryGetValue(from, out var next) && Array.IndexOf(next, to) >= 0;

    /// <summary>
    /// Throws <see cref="InvalidRideStateTransitionException"/> when the transition is not permitted.
    /// </summary>
    public static void EnsureCanTransition(RideStatus from, RideStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidRideStateTransitionException(from, to);
        }
    }
}

/// <summary>Raised when an invalid ride status transition is attempted.</summary>
public sealed class InvalidRideStateTransitionException : Exception
{
    public RideStatus From { get; }
    public RideStatus To { get; }

    public InvalidRideStateTransitionException(RideStatus from, RideStatus to)
        : base($"Invalid ride state transition from '{from}' to '{to}'.")
    {
        From = from;
        To = to;
    }
}
