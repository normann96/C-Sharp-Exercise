namespace CSharpApp.Infrastructure.Configuration;

public static class HttpConfiguration
{
    public const string PlatziApi = "PlatziApi";
    public const string PlatziAuth = "PlatziAuth";
    public const string PlatziProbe = "PlatziProbe";

    // Stated here rather than inherited from library defaults: the caller is a user-facing request, not a
    // batch job, so retries are best-effort inside the total timeout.
    private static readonly TimeSpan TotalRequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);
    private const long MaxResponseBytes = 8 * 1024 * 1024;

    // Hard outer bound for one call: the auth handler sits outside the pipeline, so a token refresh and one
    // resend can otherwise stack up to HttpClient's 100-second default.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    // A probe reports the state now; retrying or waiting longer would only hide a flapping upstream.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    public static IServiceCollection AddHttpConfiguration(this IServiceCollection services)
    {
        services.AddSingleton<ITokenProvider, AccessTokenProvider>();
        services.AddTransient<AuthTokenHandler>();

        services.AddHttpClient<IPlatziStoreClient, PlatziStoreClient>(PlatziApi, ConfigureClient)
            .UseConfiguredHandlerLifetime()
            // Outermost, so its single 401 refresh is not multiplied by the transient-retry pipeline below.
            .AddHttpMessageHandler<AuthTokenHandler>()
            .AddConfiguredResilience(retryOnlyIdempotent: true);

        // Login has no side effects, so its POST may be retried; the store client's writes may not.
        services.AddHttpClient(PlatziAuth, ConfigureClient)
            .UseConfiguredHandlerLifetime()
            .AddConfiguredResilience(retryOnlyIdempotent: false);

        services.AddHttpClient(PlatziProbe, (sp, client) =>
            {
                ConfigureClient(sp, client);
                client.Timeout = ProbeTimeout;
            })
            .UseConfiguredHandlerLifetime();

        // The health service enforces its own bound on the check, independent of the probe client's timeout, and
        // Degraded is what it reports for the two paths the check does not control: that bound firing, and a throw.
        services.AddHealthChecks()
            .AddCheck<PlatziApiHealthCheck>(PlatziApiHealthCheck.Name, failureStatus: HealthStatus.Degraded,
                tags: [PlatziApiHealthCheck.ReadyTag], timeout: ProbeTimeout + TimeSpan.FromSeconds(1));

        return services;
    }

    private static void ConfigureClient(IServiceProvider sp, HttpClient client)
    {
        var baseUrl = sp.GetRequiredService<IOptions<RestApiSettings>>().Value.BaseUrl!; // validated at startup
        // Without the trailing slash, relative paths would replace the last segment of the base path.
        client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
        client.MaxResponseContentBufferSize = MaxResponseBytes;
    }

    private static IHttpClientBuilder AddConfiguredResilience(this IHttpClientBuilder builder, bool retryOnlyIdempotent)
    {
        builder.AddStandardResilienceHandler()
            .Configure((options, sp) => ConfigureResilience(options, sp.GetRequiredService<IOptions<HttpClientSettings>>().Value, retryOnlyIdempotent));

        // The resilience handler resets the client timeout to infinite so that its own budget is the only one, and it
        // does so after the client's configure action - so the hard bound must follow it here, or it is silently lost.
        return builder.ConfigureHttpClient(client => client.Timeout = RequestTimeout);
    }

    private static IHttpClientBuilder UseConfiguredHandlerLifetime(this IHttpClientBuilder builder)
    {
        builder.Services.AddOptions<HttpClientFactoryOptions>(builder.Name)
            .Configure<IOptions<HttpClientSettings>>((o, s) => o.HandlerLifetime = TimeSpan.FromMinutes(s.Value.LifeTime));
        return builder;
    }

    private static void ConfigureResilience(HttpStandardResilienceOptions options, HttpClientSettings settings, bool retryOnlyIdempotent)
    {
        options.TotalRequestTimeout.Timeout = TotalRequestTimeout;
        options.AttemptTimeout.Timeout = AttemptTimeout;

        if (settings.RetryCount == 0)
        {
            options.Retry.ShouldHandle = static _ => PredicateResult.False(); // Polly needs at least one attempt, so "0" disables the strategy
            return;
        }

        options.Retry.MaxRetryAttempts = settings.RetryCount;
        options.Retry.Delay = TimeSpan.FromMilliseconds(settings.SleepDuration);
        options.Retry.MaxDelay = MaxRetryDelay;

        if (retryOnlyIdempotent)
        {
            // A retried write can duplicate the entity when the first attempt was processed but its answer was lost.
            var isTransient = options.Retry.ShouldHandle;
            options.Retry.ShouldHandle = async args => IsIdempotent(args.Context.GetRequestMessage()?.Method) && await isTransient(args);
        }
    }

    private static bool IsIdempotent(HttpMethod? method)
        => method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options
           || method == HttpMethod.Put || method == HttpMethod.Delete;
}
