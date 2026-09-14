using CSharpApp.Infrastructure.HealthChecks;
using CSharpApp.IntegrationTests.Common;
using CSharpApp.IntegrationTests.TestDoubles;
using Microsoft.Extensions.Logging;

namespace CSharpApp.IntegrationTests.Endpoints;

public sealed class HealthEndpointTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private const string LiveRoute = "/health/live";
    private const string ReadyRoute = "/health/ready";
    private const string ProbePath = FakePlatziHandler.ApiPrefix + "/products?limit=1&offset=0";

    [Fact]
    public async Task Live_AnswersForThisProcessOnly()
    {
        // Arrange
        var upstreamCalls = Fake.StoreRequests.Count;

        // Act
        var response = await Client.GetAsync(LiveRoute);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await ReadJsonAsync(response);
        Assert.Equal("Healthy", report.GetProperty("status").GetString());
        Assert.Empty(report.GetProperty("checks").EnumerateArray());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(upstreamCalls, Fake.StoreRequests.Count);
    }

    [Fact]
    public async Task Ready_ProbesTheUpstreamAnonymously_WithoutLoggingIn()
    {
        // Arrange
        var logins = Fake.LoginCount;

        // Act
        var response = await Client.GetAsync(ReadyRoute);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await ReadJsonAsync(response);
        Assert.Equal("Healthy", report.GetProperty("status").GetString());
        var check = Assert.Single(report.GetProperty("checks").EnumerateArray());
        Assert.Equal(PlatziApiHealthCheck.Name, check.GetProperty("name").GetString());
        var probe = Fake.LastStoreRequest;
        Assert.Equal(ProbePath, probe.PathAndQuery);
        Assert.Null(probe.Authorization);
        Assert.Equal(logins, Fake.LoginCount);
    }

    [Fact]
    public async Task Ready_WhenTheUpstreamAnswersAnError_IsDegraded_AndStill200()
    {
        // Arrange
        Fake.FailNextStoreCalls(1, HttpStatusCode.ServiceUnavailable);

        // Act
        var response = await Client.GetAsync(ReadyRoute);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await ReadJsonAsync(response);
        Assert.Equal("Degraded", report.GetProperty("status").GetString());
        var check = Assert.Single(report.GetProperty("checks").EnumerateArray());
        Assert.Equal("Degraded", check.GetProperty("status").GetString());
        Assert.Equal(503, check.GetProperty("data").GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task Ready_WhenTheUpstreamIsUnreachable_IsDegraded_WithoutTheExceptionInTheBody()
    {
        // Arrange
        Fake.ThrowOnNextStoreCall(new HttpRequestException("connection refused by upstream.test (internal detail)"));

        // Act
        var response = await Client.GetAsync(ReadyRoute);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"Degraded\"", body);
        Assert.Contains("Upstream API unreachable", body);
        Assert.DoesNotContain("internal detail", body);
    }

    [Fact]
    public async Task Ready_IsTimedQuietly()
    {
        // Arrange
        var url = ReadyRoute;

        // Act
        await Client.GetAsync(url);

        // Assert
        var timing = Assert.Single(TimingEvents());
        Assert.Equal(LogLevel.Debug, timing.Level);
        Assert.Equal(ReadyRoute, timing.Properties["RoutePattern"]);
    }
}
