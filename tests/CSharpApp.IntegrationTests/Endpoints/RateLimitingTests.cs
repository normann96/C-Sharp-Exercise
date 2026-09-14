using CSharpApp.IntegrationTests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CSharpApp.IntegrationTests.Endpoints;

public sealed class RateLimitingTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private const string HealthRoute = "/health/live";

    private WebApplicationFactory<Program> HostAllowing(int permits) => Fixture.WithWebHostBuilder(builder => builder
        .UseSetting("RateLimitingSettings:PermitLimit", permits.ToString())
        .UseSetting("RateLimitingSettings:WindowSeconds", "60"));

    [Fact]
    public async Task WithinTheAllowance_RequestsAreServed()
    {
        // Arrange
        using var limited = HostAllowing(3);
        var client = limited.CreateClient();

        // Act
        var responses = new List<HttpResponseMessage>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            responses.Add(await client.GetAsync(ProductsRoute));
        }

        // Assert
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
    }

    [Fact]
    public async Task BeyondTheAllowance_TheCallerIsRefusedWithProblemDetails()
    {
        // Arrange
        using var limited = HostAllowing(2);
        var client = limited.CreateClient();
        await client.GetAsync(ProductsRoute);
        await client.GetAsync(ProductsRoute);

        // Act
        var response = await client.GetAsync(ProductsRoute);

        // Assert
        var problem = await AssertProblemAsync(response, HttpStatusCode.TooManyRequests);
        Assert.Equal("Too many requests.", problem.GetProperty("title").GetString());
        Assert.False(problem.TryGetProperty("detail", out _));
        Assert.Equal(TimeSpan.FromSeconds(60), response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task BeyondTheAllowance_NothingIsSentUpstream()
    {
        // Arrange: the point of the limit is that a refused caller costs the upstream nothing
        using var limited = HostAllowing(1);
        var client = limited.CreateClient();
        await client.GetAsync(ProductsRoute);
        var upstreamCalls = Fake.StoreRequests.Count;

        // Act
        var response = await client.GetAsync(ProductsRoute);

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(upstreamCalls, Fake.StoreRequests.Count);
    }

    [Fact]
    public async Task HealthEndpoints_AreNeverRefused()
    {
        // Arrange: probes must answer even while a caller is being throttled
        using var limited = HostAllowing(1);
        var client = limited.CreateClient();
        await client.GetAsync(ProductsRoute);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync(ProductsRoute)).StatusCode);

        // Act
        var first = await client.GetAsync(HealthRoute);
        var second = await client.GetAsync(HealthRoute);

        // Assert
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }
}
