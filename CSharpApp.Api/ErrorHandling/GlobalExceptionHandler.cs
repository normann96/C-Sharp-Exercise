using System.Globalization;
using System.Net;
using System.Text.Json;
using CSharpApp.Core.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace CSharpApp.Api.ErrorHandling;

/// <summary>
/// The one place a failure becomes a response. The caller gets a problem details body whose status says whose
/// fault it was; the exception itself, with whatever the upstream put in its body, goes to the log only.
/// </summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        // Defence in depth: the middleware already answers a caller that went away with 499 before reaching here.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        var (status, title) = Classify(exception);
        Log(httpContext, exception, status);

        httpContext.Response.StatusCode = status;
        if (RetryAfter(exception) is { } retryAfter)
        {
            httpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        ProblemDetails problem = exception is ValidationException validation
            ? new HttpValidationProblemDetails(GroupByField(validation))
            : new ProblemDetails();
        problem.Status = status;
        problem.Title = title;
        problem.Instance = httpContext.Request.Path;

        var written = await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
        if (!written)
        {
            // No writer accepts the caller's Accept header; the status still answers, with the title as plain text.
            httpContext.Response.ContentType = "text/plain; charset=utf-8";
            await httpContext.Response.WriteAsync(title, ct);
        }

        return true;
    }

    private static (int Status, string Title) Classify(Exception exception) => exception switch
    {
        ValidationException => (StatusCodes.Status400BadRequest, "One or more validation errors occurred."),

        // Minimal-API binding throws this in Development; Production answers a bodiless 400 that status code
        // pages then fill. Mapping it keeps the status the framework intended in both environments.
        BadHttpRequestException unreadable => (unreadable.StatusCode, "The request could not be read."),

        // The client decides which upstream 4xx are about the request's content; everything else is ours.
        UpstreamRejectedRequestException => (StatusCodes.Status422UnprocessableEntity, "The upstream service could not process the request."),
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => (StatusCodes.Status503ServiceUnavailable, "The upstream service is rate limiting requests."),
        HttpRequestException or HttpIOException or JsonException => (StatusCodes.Status502BadGateway, "The upstream service call failed."),

        TimeoutRejectedException or OperationCanceledException => (StatusCodes.Status504GatewayTimeout, "The upstream service timed out."),
        BrokenCircuitException or RateLimiterRejectedException => (StatusCodes.Status503ServiceUnavailable, "The upstream service is temporarily unavailable."),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
    };

    // The circuit breaker's exception gains a RetryAfter in a later Polly than the resilience package resolves today.
    private static TimeSpan? RetryAfter(Exception exception)
        => exception is RateLimiterRejectedException { RetryAfter: { } after } ? after : null;

    private void Log(HttpContext httpContext, Exception exception, int status)
    {
        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Request {RequestMethod:l} {RequestPath:l} failed with {StatusCode}",
                httpContext.Request.Method, httpContext.Request.Path, status);
        }
        else if (status == StatusCodes.Status422UnprocessableEntity)
        {
            logger.LogWarning(exception, "Request {RequestMethod:l} {RequestPath:l} was rejected by the upstream service",
                httpContext.Request.Method, httpContext.Request.Path);
        }
    }

    private static Dictionary<string, string[]> GroupByField(ValidationException validation)
        => validation.Errors
            .GroupBy(failure => failure.PropertyName ?? string.Empty)
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray());
}
