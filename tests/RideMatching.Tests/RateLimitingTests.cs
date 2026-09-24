using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace RideMatching.Tests;

public class RateLimitingTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public RateLimitingTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Driver_creation_is_rate_limited_returns_429()
    {
        // driver-create policy: 10 permits / 10s per source. The test client shares
        // one loopback IP, so exceeding the limit should yield a 429.
        var client = _factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 15; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/drivers", new { name = $"D{i}" });
            statuses.Add(resp.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.Created, "the first requests within the window succeed");
        statuses.Should().Contain(HttpStatusCode.TooManyRequests, "requests beyond the limit are rejected with 429");
    }
}
