using CSharpApp.IntegrationTests.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace CSharpApp.IntegrationTests.Common;

/// <summary>
/// The real application booted in memory, with two substitutions: every named HttpClient ends in the in-memory
/// upstream instead of a socket, and a capturing logger sits next to Serilog. Everything else is production wiring.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>
{
    public const string Username = "integration@example.com";
    public const string Password = "integration-secret";

    public FakePlatziHandler Fake { get; } = new();
    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        // Settings go in as host configuration, so Program.cs sees them at CreateBuilder time like appsettings.
        // A reserved domain: if the fake were ever unplugged, every call would fail at DNS, never reach the sandbox.
        builder.UseSetting("RestApiSettings:BaseUrl", "https://upstream.test/api/v1/");
        builder.UseSetting("RestApiSettings:Username", Username);
        builder.UseSetting("RestApiSettings:Password", Password);
        // Real retries, no real waiting: a scripted 5xx is retried within milliseconds instead of seconds.
        builder.UseSetting("HttpClientSettings:SleepDuration", "1");

        // The provider-specific filter outranks the configured minimum levels, so Debug events are captured too.
        builder.ConfigureLogging(logging => logging.AddProvider(Logs).AddFilter<CapturingLoggerProvider>(null, LogLevel.Trace));

        builder.ConfigureTestServices(services =>
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handler => handler.PrimaryHandler = Fake)));
    }
}
