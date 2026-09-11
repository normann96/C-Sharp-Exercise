using Microsoft.Extensions.DependencyInjection;

namespace CSharpApp.Application.Configuration;

public static class ApplicationConfiguration
{
    /// <summary>
    /// Registers the request handlers and the pipeline that guards them. It lives in this layer so the
    /// infrastructure layer never has to know what the application layer contains.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var applicationAssembly = typeof(ValidationBehavior<,>).Assembly;

        services.AddMediatR(mediator =>
        {
            mediator.RegisterServicesFromAssembly(applicationAssembly);
            mediator.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        // The validators hold no state, so the rule trees are built once rather than per request.
        services.AddValidatorsFromAssembly(applicationAssembly, ServiceLifetime.Singleton);

        return services;
    }
}
