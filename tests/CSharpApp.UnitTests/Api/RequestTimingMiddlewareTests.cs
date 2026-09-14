using System.Text.RegularExpressions;
using CSharpApp.Api.Middleware;
using CSharpApp.Core.Settings;
using CSharpApp.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CSharpApp.UnitTests.Api;

public partial class RequestTimingMiddlewareTests
{
    private const string ProductsRoute = "api/v{version:apiVersion}/products/{id:int}";

    private readonly CapturingLogger<RequestTimingMiddleware> _log = new();
    private readonly RecordingResponseFeature _response = new();
    private readonly DefaultHttpContext _context = new() { Request = { Method = HttpMethods.Get, Path = "/api/v1/products/7" } };

    private RequestTimingMiddleware Middleware(RequestDelegate next, int slowThresholdMs = 1000)
        => new(next, _log, Options.Create(new PerformanceLoggingSettings { SlowRequestThresholdMs = slowThresholdMs }));

    private static RouteEndpoint Endpoint(string pattern)
        => new(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0, EndpointMetadataCollection.Empty, pattern);

    [GeneratedRegex(@"^total;dur=\d+\.\d$")]
    private static partial Regex ServerTimingValue();

    [Fact]
    public async Task LogsMethodRouteStatusAndElapsed_OnceTheResponseIsDone()
    {
        // Arrange: the endpoint is known only after routing, i.e. inside the rest of the pipeline
        var middleware = Middleware(context =>
        {
            context.SetEndpoint(Endpoint(ProductsRoute));
            context.Response.StatusCode = StatusCodes.Status201Created;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(_context);

        // Assert
        var entry = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("GET", entry.Message);
        Assert.Contains(ProductsRoute, entry.Message);   // the aggregation key: bounded cardinality
        Assert.Contains("/api/v1/products/7", entry.Message);   // the concrete path: for a single request's diagnosis
        Assert.Contains("201", entry.Message);
        Assert.Matches(@"\d+\.\d ms", entry.Message);
    }

    [Fact]
    public async Task HandledFailure_StillLogsTheRoute()
    {
        // Arrange: the exception handler clears the endpoint before it answers and keeps the original on its feature
        var middleware = Middleware(context =>
        {
            context.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature
            {
                Error = new InvalidOperationException("handled downstream"),
                Path = context.Request.Path,
                Endpoint = Endpoint(ProductsRoute),
            });
            context.SetEndpoint(null);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(_context);

        // Assert
        var entry = Assert.Single(_log.Entries);
        Assert.Contains(ProductsRoute, entry.Message);
        Assert.Contains("400", entry.Message);
    }

    [Fact]
    public async Task UnmatchedRequest_SharesOneRouteKeyAndKeepsItsPath()
    {
        // Arrange: a 404 from routing has no endpoint; a raw path as the key would give every probe its own series
        var middleware = Middleware(context => { context.Response.StatusCode = StatusCodes.Status404NotFound; return Task.CompletedTask; });

        // Act
        await middleware.InvokeAsync(_context);

        // Assert
        var entry = Assert.Single(_log.Entries);
        Assert.Contains("(unmatched)", entry.Message);
        Assert.Contains("/api/v1/products/7", entry.Message);
        Assert.Contains("404", entry.Message);
    }

    [Fact]
    public async Task SlowRequest_IsLoggedAsAWarning()
    {
        // Arrange: a threshold of one millisecond, and a request that takes longer than that
        var middleware = Middleware(async _ => await Task.Delay(30), slowThresholdMs: 1);

        // Act
        await middleware.InvokeAsync(_context);

        // Assert
        Assert.Equal(LogLevel.Warning, Assert.Single(_log.Entries).Level);
    }

    [Fact]
    public async Task QuietEndpoint_IsLoggedAtDebug_EvenWhenSlow()
    {
        // Arrange: health probes hit every few seconds and the readiness one pays an upstream round-trip
        var quiet = new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("health/ready"), 0,
            new EndpointMetadataCollection(new QuietRequestTimingMetadata()), "ready");
        var middleware = Middleware(async context => { context.SetEndpoint(quiet); await Task.Delay(30); }, slowThresholdMs: 1);

        // Act
        await middleware.InvokeAsync(_context);

        // Assert
        var entry = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Contains("health/ready", entry.Message);
    }

    [Fact]
    public async Task QuietEndpoint_ThatFailsServerSide_IsNotQuiet()
    {
        // Arrange: a probe answering 5xx means this process is broken, which is exactly what the log is for
        var quiet = new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("health/live"), 0,
            new EndpointMetadataCollection(new QuietRequestTimingMetadata()), "live");
        var middleware = Middleware(context => { context.SetEndpoint(quiet); context.Response.StatusCode = StatusCodes.Status500InternalServerError; return Task.CompletedTask; });

        // Act
        await middleware.InvokeAsync(_context);

        // Assert
        Assert.Equal(LogLevel.Information, Assert.Single(_log.Entries).Level);
    }

    [Fact]
    public async Task StillLogs_AndRethrows_WhenTheRestOfThePipelineThrows()
    {
        // Arrange: the exception handler sets the status before rethrowing; whatever is on the response is what gets logged
        var middleware = Middleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            throw new InvalidOperationException("boom");
        });

        // Act
        var act = () => middleware.InvokeAsync(_context);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains("500", Assert.Single(_log.Entries).Message);
    }

    [Fact]
    public async Task ExposesTheElapsedTimeAsAServerTimingHeader_WhenTheResponseStarts()
    {
        // Arrange: headers must be set before the first byte leaves, which is what OnStarting guarantees
        _context.Features.Set<IHttpResponseFeature>(_response);
        var middleware = Middleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(_context);
        await _response.StartAsync();

        // Assert
        Assert.Matches(ServerTimingValue(), _context.Response.Headers["Server-Timing"].ToString());
    }
}
