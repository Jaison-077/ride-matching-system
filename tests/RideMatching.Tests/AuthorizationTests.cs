using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using RideMatching.Api.Domain.Entities;
using RideMatching.Api.Domain.Enums;
using RideMatching.Api.Security;

namespace RideMatching.Tests;

public class AuthorizationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AuthorizationTests(ApiFactory factory) => _factory = factory;

    private HttpClient Client() => _factory.CreateClient();

    private HttpClient ClientAs(Guid subjectId, string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.TokenFor(subjectId, role));
        return client;
    }

    // ---- Unauthenticated ----

    [Fact]
    public async Task Unauthenticated_ride_create_returns_401()
    {
        var resp = await Client().PostAsJsonAsync("/api/rides", new
        {
            pickupLatitude = 28.6139,
            pickupLongitude = 77.2090,
            destinationLatitude = 28.5355,
            destinationLongitude = 77.3910
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unauthenticated_driver_online_returns_401()
    {
        var resp = await Client().PostAsync($"/api/drivers/{Guid.NewGuid()}/online", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- Wrong role ----

    [Fact]
    public async Task Driver_token_cannot_create_ride_403()
    {
        var client = ClientAs(Guid.NewGuid(), Roles.Driver);
        var resp = await client.PostAsJsonAsync("/api/rides", new
        {
            pickupLatitude = 28.6139,
            pickupLongitude = 77.2090,
            destinationLatitude = 28.5355,
            destinationLongitude = 77.3910
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Rider_token_cannot_go_online_403()
    {
        var riderId = Guid.NewGuid();
        var client = ClientAs(riderId, Roles.Rider);
        var resp = await client.PostAsync($"/api/drivers/{riderId}/online", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- Ownership ----

    [Fact]
    public async Task Driver_cannot_operate_on_another_driver_403()
    {
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        var client = ClientAs(me, Roles.Driver);
        var resp = await client.PostAsync($"/api/drivers/{other}/online", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Rider_cannot_view_another_riders_ride_403()
    {
        // Seed a ride owned by rider A.
        var riderA = Guid.NewGuid();
        var rideId = Guid.NewGuid();
        await _factory.WithDbAsync(async db =>
        {
            db.Rides.Add(new Ride
            {
                Id = rideId,
                RiderId = riderA,
                PickupLatitude = 28.6139,
                PickupLongitude = 77.2090,
                DestinationLatitude = 28.5355,
                DestinationLongitude = 77.3910,
                Status = RideStatus.Matching,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });

        // Rider B tries to read it.
        var client = ClientAs(Guid.NewGuid(), Roles.Rider);
        var resp = await client.GetAsync($"/api/rides/{rideId}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Rider_can_view_their_own_ride_200()
    {
        var riderA = Guid.NewGuid();
        var rideId = Guid.NewGuid();
        await _factory.WithDbAsync(async db =>
        {
            db.Rides.Add(new Ride
            {
                Id = rideId,
                RiderId = riderA,
                PickupLatitude = 28.6139,
                PickupLongitude = 77.2090,
                DestinationLatitude = 28.5355,
                DestinationLongitude = 77.3910,
                Status = RideStatus.Matching,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });

        var client = ClientAs(riderA, Roles.Rider);
        var resp = await client.GetAsync($"/api/rides/{rideId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ride_create_uses_token_identity_not_body()
    {
        var riderId = Guid.NewGuid();
        var client = ClientAs(riderId, Roles.Rider);
        var resp = await client.PostAsJsonAsync("/api/rides", new
        {
            pickupLatitude = 28.6139,
            pickupLongitude = 77.2090,
            destinationLatitude = 28.5355,
            destinationLongitude = 77.3910
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await resp.Content.ReadFromJsonAsync<CreateRideBody>();
        await _factory.WithDbAsync(async db =>
        {
            var ride = await db.Rides.FindAsync(body!.RideId);
            ride!.RiderId.Should().Be(riderId, "rider identity must come from the token");
        });
    }

    private sealed record CreateRideBody(Guid RideId, string Status);
}
