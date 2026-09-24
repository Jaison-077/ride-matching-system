using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using RideMatching.Api.Security;

namespace RideMatching.Tests;

public class SignalRAuthorizationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public SignalRAuthorizationTests(ApiFactory factory) => _factory = factory;

    private HubConnection BuildConnection(Guid subjectId, string role)
    {
        var token = _factory.TokenFor(subjectId, role);
        return new HubConnectionBuilder()
            .WithUrl($"{_factory.Server.BaseAddress}hubs/rides?access_token={token}", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
    }

    [Fact]
    public async Task Unauthenticated_connection_is_rejected()
    {
        var conn = new HubConnectionBuilder()
            .WithUrl($"{_factory.Server.BaseAddress}hubs/rides", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        var act = async () => await conn.StartAsync();
        await act.Should().ThrowAsync<Exception>("the hub requires authentication");
        await conn.DisposeAsync();
    }

    [Fact]
    public async Task Driver_can_subscribe_to_own_driver_group_but_not_another()
    {
        var driverId = Guid.NewGuid();
        var conn = BuildConnection(driverId, Roles.Driver);
        await conn.StartAsync();

        // Own driver group: allowed.
        var ownAct = async () => await conn.InvokeAsync("SubscribeToDriver", driverId);
        await ownAct.Should().NotThrowAsync();

        // Another driver's group: rejected (hub throws -> client invocation faults).
        var otherAct = async () => await conn.InvokeAsync("SubscribeToDriver", Guid.NewGuid());
        (await otherAct.Should().ThrowAsync<Exception>()).And.Message.Should().Contain("not allowed");

        await conn.DisposeAsync();
    }

    [Fact]
    public async Task Rider_cannot_subscribe_to_driver_group()
    {
        var riderId = Guid.NewGuid();
        var conn = BuildConnection(riderId, Roles.Rider);
        await conn.StartAsync();

        var act = async () => await conn.InvokeAsync("SubscribeToDriver", riderId);
        (await act.Should().ThrowAsync<Exception>("a rider is not a driver"))
            .And.Message.Should().Contain("not allowed");

        await conn.DisposeAsync();
    }

    [Fact]
    public async Task Rider_can_subscribe_to_own_rider_group_but_not_another()
    {
        var riderId = Guid.NewGuid();
        var conn = BuildConnection(riderId, Roles.Rider);
        await conn.StartAsync();

        var ownAct = async () => await conn.InvokeAsync("SubscribeToRider", riderId);
        await ownAct.Should().NotThrowAsync();

        var otherAct = async () => await conn.InvokeAsync("SubscribeToRider", Guid.NewGuid());
        (await otherAct.Should().ThrowAsync<Exception>()).And.Message.Should().Contain("not allowed");

        await conn.DisposeAsync();
    }
}
