using System.Net;
using CSharpApp.Core.Settings;
using CSharpApp.Infrastructure.HealthChecks;
using CSharpApp.UnitTests.Common;
using CSharpApp.UnitTests.TestDoubles;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CSharpApp.UnitTests.HealthChecks;

public class PlatziApiHealthCheckTests : HttpTestBase
{
    private readonly IOptions<RestApiSettings> _settings = Options.Create(new RestApiSettings
    {
        BaseUrl = BaseUrl, Products = "/products", Categories = "categories",
        Auth = "/auth/login", Username = "u", Password = "p"
    });

    private (PlatziApiHealthCheck Check, StubHttpMessageHandler Stub, HealthCheckContext Context) Create(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var (http, stub) = StubbedHttpClient(responder);
        var check = new PlatziApiHealthCheck(new StubHttpClientFactory(http), _settings);
        var context = new HealthCheckContext { Registration = new HealthCheckRegistration(PlatziApiHealthCheck.Name, check, HealthStatus.Degraded, [PlatziApiHealthCheck.ReadyTag]) };
        return (check, stub, context);
    }

    [Fact]
    public async Task ReachableUpstream_IsHealthy_AndTheProbeIsCheapAndAnonymous()
    {
        // Arrange
        var (check, stub, context) = Create(_ => Json("[]"));

        // Act
        var result = await check.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        var request = Assert.Single(stub.Requests);
        Assert.Equal("https://api.escuelajs.co/api/v1/products?limit=1&offset=0", request.RequestUri!.ToString());
        Assert.Null(request.Headers.Authorization);   // a reachability probe must not depend on the login working
    }

    [Fact]
    public async Task UpstreamError_IsDegraded_NotUnhealthy()
    {
        // Arrange: a proxy without its upstream is impaired, not dead; an orchestrator must not restart it for that
        var (check, _, context) = Create(_ => Status(HttpStatusCode.ServiceUnavailable));

        // Act
        var result = await check.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(503, result.Data["statusCode"]);
    }

    [Fact]
    public async Task UnreachableUpstream_IsDegraded_AndNeverThrows()
    {
        // Arrange
        var (check, _, context) = Create(_ => throw new HttpRequestException("Connection refused"));

        // Act
        var result = await check.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("unreachable", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeBudgetExpired_IsDegraded()
    {
        // Arrange: HttpClient.Timeout surfaces as a cancellation whose token was never cancelled by the caller
        var (check, _, context) = Create(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout", new TimeoutException()));

        // Act
        var result = await check.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("unreachable", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        // Arrange: a prober that gave up says nothing about the upstream, so it must not be reported as its state
        var (check, _, context) = Create(_ => Json("[]"));

        // Act
        var act = () => check.CheckHealthAsync(context, new CancellationToken(canceled: true));

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }
}
