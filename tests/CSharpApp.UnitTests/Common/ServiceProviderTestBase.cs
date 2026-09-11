using CSharpApp.Application.Configuration;
using CSharpApp.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CSharpApp.UnitTests.Common;

/// <summary>
/// Builds real service providers from in-memory configuration, the way the app does, without a host.
/// Every provider built during a test is disposed when the test ends.
/// </summary>
public abstract class ServiceProviderTestBase : HttpTestBase, IDisposable
{
    private readonly List<ServiceProvider> _providers = [];

    protected static Dictionary<string, string?> ValidConfiguration() => new()
    {
        ["RestApiSettings:BaseUrl"] = BaseUrl,
        ["RestApiSettings:Products"] = "products",
        ["RestApiSettings:Categories"] = "/categories",
        ["RestApiSettings:Auth"] = "/auth/login",
        ["RestApiSettings:Username"] = "u",
        ["RestApiSettings:Password"] = "p",
        ["HttpClientSettings:LifeTime"] = "7",
        ["HttpClientSettings:RetryCount"] = "2",
        ["HttpClientSettings:SleepDuration"] = "100",
    };

    protected ServiceProvider BuildProvider(Dictionary<string, string?>? configuration = null, Action<IServiceCollection>? configureServices = null)
    {
        var configurationRoot = new ConfigurationBuilder().AddInMemoryCollection(configuration ?? ValidConfiguration()).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configurationRoot);
        services.AddLogging();
        services.AddApplication();
        services.AddDefaultConfiguration();
        configureServices?.Invoke(services);

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
