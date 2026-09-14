namespace CSharpApp.Infrastructure.Configuration;

public static class DefaultConfiguration
{
    public static IServiceCollection AddDefaultConfiguration(this IServiceCollection services)
    {
        // Validated at host start: bad or misspelled configuration stops the process before it serves a request.
        services.AddOptionsWithValidateOnStart<RestApiSettings, RestApiSettingsValidator>()
            .BindConfiguration(nameof(RestApiSettings), o => o.ErrorOnUnknownConfiguration = true);

        services.AddOptionsWithValidateOnStart<HttpClientSettings, HttpClientSettingsValidator>()
            .BindConfiguration(nameof(HttpClientSettings), o => o.ErrorOnUnknownConfiguration = true);

        services.AddOptionsWithValidateOnStart<PerformanceLoggingSettings, PerformanceLoggingSettingsValidator>()
            .BindConfiguration(nameof(PerformanceLoggingSettings), o => o.ErrorOnUnknownConfiguration = true);

        services.AddOptionsWithValidateOnStart<RateLimitingSettings, RateLimitingSettingsValidator>()
            .BindConfiguration(nameof(RateLimitingSettings), o => o.ErrorOnUnknownConfiguration = true);

        return services;
    }
}
