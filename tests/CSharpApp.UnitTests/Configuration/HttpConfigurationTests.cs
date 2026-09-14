using System.Net;
using CSharpApp.Core.Interfaces;
using CSharpApp.Infrastructure.Configuration;
using CSharpApp.Infrastructure.HealthChecks;
using CSharpApp.Infrastructure.Http;
using CSharpApp.UnitTests.Common;
using CSharpApp.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace CSharpApp.UnitTests.Configuration;

public class HttpConfigurationTests : ServiceProviderTestBase
{
    private readonly StubHttpMessageHandler _unavailableUpstream = new(_ => Status(HttpStatusCode.ServiceUnavailable));

    // The store client carries a bearer token, so its pipeline asks the token provider to log in.
    private readonly StubHttpMessageHandler _workingLogin = new(_ => Json(LoginBody(), HttpStatusCode.Created));

    private static string LoginBody()
    {
        var token = TestJwt.WithExpiry(DateTimeOffset.UtcNow.AddHours(1));
        return $$"""{"access_token":"{{token}}","refresh_token":"r"}""";
    }

    private ServiceProvider BuildHttpProvider(string baseUrl = BaseUrl, int retryCount = 2, int sleepDuration = 100, string? clientWithStubbedUpstream = null, ITokenProvider? replaceTokenProvider = null)
    {
        var configuration = ValidConfiguration();
        configuration["RestApiSettings:BaseUrl"] = baseUrl;
        configuration["HttpClientSettings:RetryCount"] = retryCount.ToString();
        configuration["HttpClientSettings:SleepDuration"] = sleepDuration.ToString();

        return BuildProvider(configuration, services =>
        {
            services.AddHttpConfiguration();
            if (replaceTokenProvider is not null)
            {
                services.AddSingleton(replaceTokenProvider);
            }

            if (clientWithStubbedUpstream is null)
            {
                return;
            }

            // Keep the login offline whenever the client under test is not the login client itself.
            if (clientWithStubbedUpstream != HttpConfiguration.PlatziAuth)
            {
                services.AddHttpClient(HttpConfiguration.PlatziAuth).ConfigurePrimaryHttpMessageHandler(() => _workingLogin);
            }

            services.AddHttpClient(clientWithStubbedUpstream).ConfigurePrimaryHttpMessageHandler(() => _unavailableUpstream);
        });
    }

    private static HttpClient CreateClient(ServiceProvider provider, string name)
        => provider.GetRequiredService<IHttpClientFactory>().CreateClient(name);

    [Theory]
    [InlineData("https://api.escuelajs.co/api/v1/", HttpConfiguration.PlatziApi)]
    [InlineData("https://api.escuelajs.co/api/v1", HttpConfiguration.PlatziApi)]
    [InlineData("https://api.escuelajs.co/api/v1", HttpConfiguration.PlatziAuth)]
    [InlineData("https://api.escuelajs.co/api/v1", HttpConfiguration.PlatziProbe)]
    public void BaseAddress_AlwaysEndsWithASlash(string configuredBaseUrl, string clientName)
    {
        // Arrange: without the trailing slash "products" would resolve to /api/products and silently drop the version segment
        var provider = BuildHttpProvider(configuredBaseUrl);

        // Act
        var client = CreateClient(provider, clientName);

        // Assert
        Assert.Equal(BaseUrl, client.BaseAddress!.ToString());
    }

    [Fact]
    public void TypedClient_ResolvesFromTheContainer()
    {
        // Arrange
        var provider = BuildHttpProvider();

        // Act
        var client = provider.GetRequiredService<IPlatziStoreClient>();

        // Assert
        Assert.IsType<PlatziStoreClient>(client);
    }

    [Fact]
    public void TokenProvider_IsASingletonSoOneIdentityServesTheWholeProcess()
    {
        // Arrange
        var provider = BuildHttpProvider();

        // Act
        var first = provider.GetRequiredService<ITokenProvider>();
        var second = provider.GetRequiredService<ITokenProvider>();

        // Assert
        Assert.IsType<AccessTokenProvider>(first);
        Assert.Same(first, second);
    }

    [Theory]
    [InlineData(HttpConfiguration.PlatziApi)]
    [InlineData(HttpConfiguration.PlatziAuth)]
    [InlineData(HttpConfiguration.PlatziProbe)]
    public void HandlerLifetime_ComesFromSettings(string clientName)
    {
        // Arrange
        var provider = BuildHttpProvider();

        // Act
        var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(clientName);

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(7), options.HandlerLifetime);
    }

    [Theory]
    [InlineData(HttpConfiguration.PlatziApi, 60)]
    [InlineData(HttpConfiguration.PlatziAuth, 60)]
    [InlineData(HttpConfiguration.PlatziProbe, 3)]
    public void EachClient_HasItsOwnHardTimeout(string clientName, int seconds)
    {
        // Arrange: a probe reports the state now, so its budget is a fraction of a real call's
        var provider = BuildHttpProvider();

        // Act
        var client = CreateClient(provider, clientName);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(seconds), client.Timeout);
    }

    [Fact]
    public void RetryOptions_ComeFromSettings()
    {
        // Arrange
        var provider = BuildHttpProvider(retryCount: 4, sleepDuration: 250);

        // Act: "<client name>-standard" is the name the standard resilience handler gives its options
        var options = provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>().Get($"{HttpConfiguration.PlatziApi}-standard");

        // Assert
        Assert.Equal(4, options.Retry.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(250), options.Retry.Delay);
    }

    [Fact]
    public async Task StoreClientRequests_CarryABearerToken()
    {
        // Arrange
        var provider = BuildHttpProvider(sleepDuration: 0, clientWithStubbedUpstream: HttpConfiguration.PlatziApi);
        var client = CreateClient(provider, HttpConfiguration.PlatziApi);

        // Act
        using var response = await client.GetAsync("products");

        // Assert
        Assert.Single(_workingLogin.Requests);
        Assert.StartsWith("Bearer ", _unavailableUpstream.Requests[0].Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task AuthHandler_SitsOutsideTheRetryPipeline()
    {
        // Arrange: the token is fetched once per call, not once per transient retry
        var tokenProvider = new FakeTokenProvider("token");
        var provider = BuildHttpProvider(retryCount: 2, sleepDuration: 0, clientWithStubbedUpstream: HttpConfiguration.PlatziApi,
            replaceTokenProvider: tokenProvider);
        var client = CreateClient(provider, HttpConfiguration.PlatziApi);

        // Act
        using var response = await client.GetAsync("products");

        // Assert
        Assert.Equal(3, _unavailableUpstream.Requests.Count);
        Assert.Equal(1, tokenProvider.TokenRequests);
    }

    [Fact]
    public async Task ReadinessProbe_IsRegistered_TalksToTheUpstreamWithoutLoggingIn_AndReportsDegraded()
    {
        // Arrange: the probe client answers 503 and the login stub is in place, so any login would be visible
        var provider = BuildHttpProvider(sleepDuration: 0, clientWithStubbedUpstream: HttpConfiguration.PlatziProbe);
        var healthChecks = provider.GetRequiredService<HealthCheckService>();

        // Act
        var report = await healthChecks.CheckHealthAsync(registration => registration.Tags.Contains(PlatziApiHealthCheck.ReadyTag));

        // Assert
        var entry = Assert.Single(report.Entries);
        Assert.Equal(PlatziApiHealthCheck.Name, entry.Key);
        Assert.Equal(HealthStatus.Degraded, entry.Value.Status);
        Assert.Single(_unavailableUpstream.Requests);   // one probe, no retries: a probe reports the state now
        Assert.Empty(_workingLogin.Requests);
        Assert.Null(_unavailableUpstream.Requests[0].Headers.Authorization);
    }

    [Theory]
    [InlineData(2, 3)]
    [InlineData(0, 1)]
    public async Task Get_TransientFailure_IsRetriedPerSettings(int retryCount, int expectedAttempts)
    {
        // Arrange: the real pipeline over a stub upstream, zero back-off
        var provider = BuildHttpProvider(retryCount: retryCount, sleepDuration: 0, clientWithStubbedUpstream: HttpConfiguration.PlatziApi);
        var client = CreateClient(provider, HttpConfiguration.PlatziApi);

        // Act
        using var response = await client.GetAsync("products");

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(expectedAttempts, _unavailableUpstream.Requests.Count);
    }

    [Fact]
    public async Task Post_TransientFailure_IsNotRetriedOnTheStoreClient()
    {
        // Arrange: a retried create could duplicate the entity
        var provider = BuildHttpProvider(sleepDuration: 0, clientWithStubbedUpstream: HttpConfiguration.PlatziApi);
        var client = CreateClient(provider, HttpConfiguration.PlatziApi);

        // Act
        using var response = await client.PostAsync("products", new StringContent("{}"));

        // Assert
        Assert.Single(_unavailableUpstream.Requests);
    }

    [Fact]
    public async Task Post_TransientFailure_IsRetriedOnTheAuthClient()
    {
        // Arrange: login has no side effects, so its POST may be retried
        var provider = BuildHttpProvider(sleepDuration: 0, clientWithStubbedUpstream: HttpConfiguration.PlatziAuth);
        var client = CreateClient(provider, HttpConfiguration.PlatziAuth);

        // Act
        using var response = await client.PostAsync("auth/login", new StringContent("{}"));

        // Assert
        Assert.Equal(3, _unavailableUpstream.Requests.Count);
    }
}
