using System.Net;
using CSharpApp.Core.Interfaces;
using CSharpApp.Infrastructure.Configuration;
using CSharpApp.Infrastructure.Http;
using CSharpApp.UnitTests.Common;
using CSharpApp.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace CSharpApp.UnitTests.Configuration;

public class HttpConfigurationTests : ServiceProviderTestBase
{
    private readonly StubHttpMessageHandler _unavailableUpstream = new(_ => Status(HttpStatusCode.ServiceUnavailable));

    private ServiceProvider BuildHttpProvider(string baseUrl = BaseUrl, int retryCount = 2, int sleepDuration = 100, string? clientWithStubbedUpstream = null)
    {
        var configuration = ValidConfiguration();
        configuration["RestApiSettings:BaseUrl"] = baseUrl;
        configuration["HttpClientSettings:RetryCount"] = retryCount.ToString();
        configuration["HttpClientSettings:SleepDuration"] = sleepDuration.ToString();

        return BuildProvider(configuration, services =>
        {
            services.AddHttpConfiguration();
            if (clientWithStubbedUpstream is not null)
            {
                services.AddHttpClient(clientWithStubbedUpstream).ConfigurePrimaryHttpMessageHandler(() => _unavailableUpstream);
            }
        });
    }

    private static HttpClient CreateClient(ServiceProvider provider, string name)
        => provider.GetRequiredService<IHttpClientFactory>().CreateClient(name);

    [Theory]
    [InlineData("https://api.escuelajs.co/api/v1/", HttpConfiguration.PlatziApi)]
    [InlineData("https://api.escuelajs.co/api/v1", HttpConfiguration.PlatziApi)]
    [InlineData("https://api.escuelajs.co/api/v1", HttpConfiguration.PlatziAuth)]
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

    [Theory]
    [InlineData(HttpConfiguration.PlatziApi)]
    [InlineData(HttpConfiguration.PlatziAuth)]
    public void HandlerLifetime_ComesFromSettings(string clientName)
    {
        // Arrange
        var provider = BuildHttpProvider();

        // Act
        var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(clientName);

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(7), options.HandlerLifetime);
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
