using System.Diagnostics;
using System.Globalization;
using CSharpApp.Core.Settings;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace CSharpApp.Api.Middleware;

/// <summary>
/// Times every request and reports it twice: as a structured log event and, to the caller, as a Server-Timing
/// header. Sits outermost among the application's middleware, so the measurement and the logged status include
/// error handling.
/// </summary>
public sealed class RequestTimingMiddleware(
    RequestDelegate next,
    ILogger<RequestTimingMiddleware> logger,
    IOptions<PerformanceLoggingSettings> options)
{
    private const string ServerTimingHeader = "Server-Timing";
    private const string ServerTimingMetric = "total";

    // Unmatched requests share one key: a raw path would give every scanner probe its own series.
    private const string UnmatchedRoute = "(unmatched)";

    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp(); // a timestamp, not a Stopwatch instance: the timing itself allocates nothing

        context.Response.OnStarting(() =>
        {
            // Headers are sealed once the first byte leaves, so the header can only cover the time up to that point.
            var soFar = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            context.Response.Headers[ServerTimingHeader] = $"{ServerTimingMetric};dur={soFar.ToString("0.0", CultureInfo.InvariantCulture)}";
            return Task.CompletedTask;
        });

        try
        {
            await next(context);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            // The route pattern is the aggregation key: it has bounded cardinality, unlike a path that carries ids.
            // The exception handler clears the endpoint before it answers, but keeps the original on its feature.
            var endpoint = context.GetEndpoint() ?? context.Features.Get<IExceptionHandlerFeature>()?.Endpoint;
            var route = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? UnmatchedRoute;

            var quiet = endpoint?.Metadata.GetMetadata<QuietRequestTimingMetadata>() is not null
                        && context.Response.StatusCode < StatusCodes.Status500InternalServerError;
            var level = quiet ? LogLevel.Debug
                : elapsed > options.Value.SlowRequestThresholdMs ? LogLevel.Warning
                : LogLevel.Information;
            logger.Log(level, "HTTP {RequestMethod:l} {RoutePattern:l} responded {StatusCode} in {ElapsedMilliseconds:0.0} ms ({RequestPath:l})",
                context.Request.Method, route, context.Response.StatusCode, elapsed, context.Request.Path);
        }
    }
}
