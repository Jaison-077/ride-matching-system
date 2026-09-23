using FluentAssertions;
using RideMatching.Api.Domain;
using RideMatching.Api.Domain.Enums;

namespace RideMatching.Tests;

public class RideStateMachineTests
{
    [Theory]
    [InlineData(RideStatus.Requested, RideStatus.Matching)]
    [InlineData(RideStatus.Requested, RideStatus.Cancelled)]
    [InlineData(RideStatus.Matching, RideStatus.Matched)]
    [InlineData(RideStatus.Matching, RideStatus.NoDriverAvailable)]
    [InlineData(RideStatus.Matching, RideStatus.Cancelled)]
    [InlineData(RideStatus.Matched, RideStatus.Completed)]
    [InlineData(RideStatus.Matched, RideStatus.Cancelled)]
    public void Valid_transitions_are_allowed(RideStatus from, RideStatus to) =>
        RideStateMachine.CanTransition(from, to).Should().BeTrue();

    [Theory]
    [InlineData(RideStatus.Requested, RideStatus.Matched)]
    [InlineData(RideStatus.Requested, RideStatus.Completed)]
    [InlineData(RideStatus.Matching, RideStatus.Completed)]
    [InlineData(RideStatus.Matched, RideStatus.Matching)]
    [InlineData(RideStatus.Completed, RideStatus.Matched)]
    [InlineData(RideStatus.Cancelled, RideStatus.Matching)]
    [InlineData(RideStatus.NoDriverAvailable, RideStatus.Matching)]
    public void Invalid_transitions_are_rejected(RideStatus from, RideStatus to) =>
        RideStateMachine.CanTransition(from, to).Should().BeFalse();

    [Fact]
    public void EnsureCanTransition_throws_on_invalid()
    {
        var act = () => RideStateMachine.EnsureCanTransition(RideStatus.Completed, RideStatus.Matching);
        act.Should().Throw<InvalidRideStateTransitionException>();
    }

    [Fact]
    public void EnsureCanTransition_passes_on_valid()
    {
        var act = () => RideStateMachine.EnsureCanTransition(RideStatus.Matching, RideStatus.Matched);
        act.Should().NotThrow();
    }
}
