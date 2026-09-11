using System.Net;
using System.Text.Json;
using CSharpApp.Api.ErrorHandling;
using CSharpApp.Core.Exceptions;
using CSharpApp.UnitTests.TestDoubles;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace CSharpApp.UnitTests.Api;

public class GlobalExceptionHandlerTests
{
    private const string UpstreamBody = "UPSTREAM-BODY-THAT-MUST-NOT-LEAK";
    private const string RequestPath = "/api/v1/products";

    private readonly CapturingProblemDetailsService _problemDetails = new();
    private readonly CapturingLogger<GlobalExceptionHandler> _logger = new();

    private readonly DefaultHttpContext _context = new() { Response = { Body = new MemoryStream() } };

    private async Task<(bool Handled, int Status, ProblemDetails? Problem)> HandleAsync(Exception exception, bool requestAborted = false)
    {
        var context = _context;
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = RequestPath;
        if (requestAborted)
        {
            using var aborted = new CancellationTokenSource();
            await aborted.CancelAsync();
            context.RequestAborted = aborted.Token;
        }

        var handler = new GlobalExceptionHandler(_problemDetails, _logger);
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);
        return (handled, context.Response.StatusCode, _problemDetails.Written?.ProblemDetails);
    }

    private static HttpRequestException Upstream(HttpStatusCode? status)
        => new($"Upstream API rejected the request with {(int?)status}: {UpstreamBody}", null, status);

    private static UpstreamRejectedRequestException Rejected(HttpStatusCode status)
        => new($"Upstream API rejected the request with {(int)status}: {UpstreamBody}", status);

    private async Task<string> ResponseBodyAsync()
    {
        _context.Response.Body.Position = 0;
        return await new StreamReader(_context.Response.Body).ReadToEndAsync();
    }

    public static TheoryData<Exception, int> StatusMapping => new()
    {
        { Rejected(HttpStatusCode.BadRequest), StatusCodes.Status422UnprocessableEntity },   // the client marked it as about the content
        { Rejected(HttpStatusCode.NotFound), StatusCodes.Status422UnprocessableEntity },
        { Upstream(HttpStatusCode.BadRequest), StatusCodes.Status502BadGateway },             // an unmarked 4xx: our path or our request
        { Upstream(HttpStatusCode.NotFound), StatusCodes.Status502BadGateway },
        { Upstream(HttpStatusCode.Unauthorized), StatusCodes.Status502BadGateway },           // our credentials, not the caller's
        { Upstream(HttpStatusCode.Forbidden), StatusCodes.Status502BadGateway },
        { new HttpIOException(HttpRequestError.ResponseEnded), StatusCodes.Status502BadGateway }, // the connection dropped mid-body
        { Upstream(HttpStatusCode.TooManyRequests), StatusCodes.Status503ServiceUnavailable },
        { Upstream(HttpStatusCode.InternalServerError), StatusCodes.Status502BadGateway },
        { Upstream(null), StatusCodes.Status502BadGateway },                                  // connection refused, DNS, TLS
        { new JsonException("unexpected token"), StatusCodes.Status502BadGateway },           // a 200 with a non-JSON body
        { new TimeoutRejectedException(), StatusCodes.Status504GatewayTimeout },
        { new BrokenCircuitException(), StatusCodes.Status503ServiceUnavailable },
        { new RateLimiterRejectedException(), StatusCodes.Status503ServiceUnavailable },
        { new OperationCanceledException(), StatusCodes.Status504GatewayTimeout },            // cancelled by the pipeline, not the caller
        { new InvalidOperationException("boom"), StatusCodes.Status500InternalServerError },
        { new BadHttpRequestException("Failed to read parameter \"CreateProductRequest request\" from the request body as JSON.", StatusCodes.Status400BadRequest), StatusCodes.Status400BadRequest },
        { new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge), StatusCodes.Status413PayloadTooLarge },
    };

    [Theory]
    [MemberData(nameof(StatusMapping))]
    public async Task MapsEachFailureClassToItsStatus(Exception exception, int expectedStatus)
    {
        // Act
        var (handled, status, problem) = await HandleAsync(exception);

        // Assert
        Assert.True(handled);
        Assert.Equal(expectedStatus, status);
        Assert.Equal(expectedStatus, problem!.Status);
        Assert.Equal(RequestPath, problem.Instance);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
    }

    [Theory]
    [MemberData(nameof(StatusMapping))]
    public async Task NeverEchoesTheExceptionMessageToTheCaller(Exception exception, int _)
    {
        // Arrange: upstream bodies and internal messages are diagnostics for the log, not for the client
        var marker = exception.Message;

        // Act
        var (_, _, problem) = await HandleAsync(exception);

        // Assert
        var serialized = JsonSerializer.Serialize(problem, problem!.GetType());
        Assert.DoesNotContain(UpstreamBody, serialized);
        if (!string.IsNullOrEmpty(marker))
        {
            Assert.DoesNotContain(marker, serialized);
        }
    }

    [Fact]
    public async Task ValidationFailure_Becomes400WithErrorsGroupedByField()
    {
        // Arrange
        var exception = new ValidationException(
        [
            new ValidationFailure("title", "'title' must not be empty."),
            new ValidationFailure("title", "The length of 'title' must be 200 characters or fewer."),
            new ValidationFailure("price", "'price' must be greater than '0'."),
        ]);

        // Act
        var (handled, status, problem) = await HandleAsync(exception);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        var validation = Assert.IsType<HttpValidationProblemDetails>(problem);
        Assert.Equal(["price", "title"], validation.Errors.Keys.Order());
        Assert.Equal(2, validation.Errors["title"].Length);
    }

    [Fact]
    public async Task RefusedWriter_StillAnswersWithTheStatusAndAPlainTextTitle()
    {
        // Arrange: no problem-details writer accepts the caller's Accept header; the failure is still the caller's
        _problemDetails.CanWrite = false;

        // Act
        var (handled, status, problem) = await HandleAsync(new ValidationException([new ValidationFailure("title", "empty")]));

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Null(problem);
        Assert.Equal("One or more validation errors occurred.", await ResponseBodyAsync());
        Assert.StartsWith("text/plain", _context.Response.ContentType);
    }

    [Fact]
    public async Task ValidationFailureWithoutAPropertyName_IsGroupedUnderAnEmptyKey()
    {
        // Arrange: a rule that forgot OverridePropertyName must degrade to a key, not to an exception in the handler
        var exception = new ValidationException([new ValidationFailure(null, "cross-field rule failed")]);

        // Act
        var (_, _, problem) = await HandleAsync(exception);

        // Assert
        var validation = Assert.IsType<HttpValidationProblemDetails>(problem);
        Assert.Equal([""], validation.Errors.Keys);
    }

    [Fact]
    public async Task RateLimited_TellsTheCallerWhenToRetry()
    {
        // Arrange
        var exception = new RateLimiterRejectedException(TimeSpan.FromSeconds(4.2));

        // Act
        var (_, status, _) = await HandleAsync(exception);

        // Assert
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal("5", _context.Response.Headers.RetryAfter);
    }

    [Fact]
    public async Task CancellationByTheCaller_IsLeftToTheFramework()
    {
        // Arrange: defence in depth - the middleware answers an abandoned request with 499 before any handler runs
        var exception = new OperationCanceledException();

        // Act
        var (handled, _, problem) = await HandleAsync(exception, requestAborted: true);

        // Assert
        Assert.False(handled);
        Assert.Null(problem);
    }

    [Theory]
    [InlineData(StatusCodes.Status500InternalServerError, true)]
    [InlineData(StatusCodes.Status502BadGateway, true)]
    [InlineData(StatusCodes.Status422UnprocessableEntity, false)]
    [InlineData(StatusCodes.Status400BadRequest, false)]
    public async Task ServerSideFailures_AreLoggedAsErrors_ClientFaultsAreNot(int status, bool expectErrorLog)
    {
        // Arrange
        Exception exception = status switch
        {
            StatusCodes.Status500InternalServerError => new InvalidOperationException("boom"),
            StatusCodes.Status502BadGateway => Upstream(HttpStatusCode.InternalServerError),
            StatusCodes.Status422UnprocessableEntity => Rejected(HttpStatusCode.BadRequest),
            _ => new ValidationException([new ValidationFailure("title", "empty")]),
        };

        // Act
        await HandleAsync(exception);

        // Assert
        Assert.Equal(expectErrorLog, _logger.Entries.Any(entry => entry.Level == LogLevel.Error));
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains(UpstreamBody)); // the body is on the exception object, not in the message template
    }
}
