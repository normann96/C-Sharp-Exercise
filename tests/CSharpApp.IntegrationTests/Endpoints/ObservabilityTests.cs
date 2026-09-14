using CSharpApp.IntegrationTests.Common;
using CSharpApp.IntegrationTests.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace CSharpApp.IntegrationTests.Endpoints;

public sealed class ObservabilityTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    // The aggregation key is the route pattern, so ids and query strings never fan out into their own series.
    private const string ProductsPattern = "/api/v{version:apiVersion}/products/";
    private const string ProductByIdPattern = "/api/v{version:apiVersion}/products/{id:int}";
    private const string UnmatchedPattern = "(unmatched)";

    [Theory]
    [InlineData(null, HttpStatusCode.OK)]
    [InlineData(0, HttpStatusCode.BadRequest)]
    [InlineData(FakePlatziHandler.UnknownId, HttpStatusCode.NotFound)]
    public async Task EveryResponse_CarriesServerTiming(int? id, HttpStatusCode expected)
    {
        // Arrange
        var url = id is null ? ProductsRoute : $"{ProductsRoute}/{id}";

        // Act
        var response = await Client.GetAsync(url);

        // Assert
        Assert.Equal(expected, response.StatusCode);
        AssertServerTiming(response);
    }

    [Fact]
    public async Task NotFound_IsTimedUnderItsRoutePattern()
    {
        // Arrange
        var url = $"{ProductsRoute}/{FakePlatziHandler.UnknownId}";

        // Act
        await Client.GetAsync(url);

        // Assert
        var timing = Assert.Single(TimingEvents());
        Assert.Equal(LogLevel.Information, timing.Level);
        Assert.Equal("GET", timing.Properties["RequestMethod"]);
        Assert.Equal(ProductByIdPattern, timing.Properties["RoutePattern"]);
        Assert.Equal(404, timing.Properties["StatusCode"]);
    }

    [Fact]
    public async Task UpstreamFailure_IsTimedUnderItsRoutePattern_WithTheStatusTheCallerReceived()
    {
        // Arrange: the exception handler rewrites this response, so the timing has to survive that too
        Fake.FailNextStoreCalls(UpstreamAttempts, HttpStatusCode.InternalServerError);

        // Act
        var response = await Client.GetAsync(ProductsRoute);

        // Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        AssertServerTiming(response);
        var timing = Assert.Single(TimingEvents());
        Assert.Equal(ProductsPattern, timing.Properties["RoutePattern"]);
        Assert.Equal(502, timing.Properties["StatusCode"]);
    }

    [Fact]
    public async Task UnknownRoute_IsTimedAsUnmatched()
    {
        // Arrange
        var url = "/nope";

        // Act
        var response = await Client.GetAsync(url);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var timing = Assert.Single(TimingEvents());
        Assert.Equal(UnmatchedPattern, timing.Properties["RoutePattern"]);
    }

    [Fact]
    public async Task SlowRequest_EscalatesToWarning()
    {
        // Arrange: a host whose threshold every real request exceeds
        using var strictHost = Fixture.WithWebHostBuilder(builder => builder.UseSetting("PerformanceLoggingSettings:SlowRequestThresholdMs", "1"));
        var client = strictHost.CreateClient();

        // Act
        var response = await client.GetAsync(ProductsRoute);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var timing = Assert.Single(TimingEvents());
        Assert.Equal(LogLevel.Warning, timing.Level);
    }
}
