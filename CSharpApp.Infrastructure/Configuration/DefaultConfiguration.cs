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

        services.AddSingleton<IProductsService, ProductsService>(); // replaced by the typed client and CQRS handlers in later steps

        return services;
    }
}
