namespace CSharpApp.Api.Extensions;

public static class LoggingExtensions
{
    private const string SinkSection = "Serilog:WriteTo";

    /// <summary>Makes the configured Serilog logger the only logging provider, refusing to start without a sink.</summary>
    public static WebApplicationBuilder AddLoggingConfiguration(this WebApplicationBuilder builder)
    {
        EnsureSinkIsConfigured(builder);

        var logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();
        builder.Logging.ClearProviders().AddSerilog(logger, dispose: true);
        return builder;
    }

    // Sinks are declared per environment, so an environment with no settings file of its own would serve traffic and
    // log nothing at all. A service nobody can observe is worse than one that refuses to start.
    private static void EnsureSinkIsConfigured(WebApplicationBuilder builder)
    {
        if (builder.Configuration.GetSection(SinkSection).Exists())
        {
            return;
        }

        var environment = builder.Environment.EnvironmentName;
        throw new InvalidOperationException($"No Serilog sink is configured for environment '{environment}'; add appsettings.{environment}.json.");
    }
}
