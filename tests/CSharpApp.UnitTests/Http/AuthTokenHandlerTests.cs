using System.Net;
using System.Net.Http.Headers;
using CSharpApp.Infrastructure.Http;
using CSharpApp.UnitTests.Common;
using CSharpApp.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSharpApp.UnitTests.Http;

public class AuthTokenHandlerTests : HttpTestBase
{
    private const string FirstToken = "first-token";
    private const string SecondToken = "second-token";
    private const string RequestBody = """{"title":"Created once"}""";

    private readonly FakeTokenProvider _tokenProvider = new(FirstToken, SecondToken);

    private (HttpClient Client, StubHttpMessageHandler Stub) CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var stub = new StubHttpMessageHandler(responder);
        var handler = new AuthTokenHandler(_tokenProvider, NullLogger<AuthTokenHandler>.Instance) { InnerHandler = stub };
        return (new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) }, stub);
    }

    private static string? AuthorizationOf(HttpRequestMessage request) => request.Headers.Authorization?.ToString();

    [Fact]
    public async Task AttachesTheAccessTokenAsABearerHeader()
    {
        // Arrange
        var (client, stub) = CreateClient(_ => Json("[]"));

        // Act
        using var response = await client.GetAsync("products");

        // Assert
        Assert.Equal($"Bearer {FirstToken}", AuthorizationOf(stub.Requests[0]));
        Assert.Equal(0, _tokenProvider.InvalidateCount);
    }

    [Fact]
    public async Task Unauthorized_InvalidatesTheTokenAndRetriesOnceWithAFreshOne()
    {
        // Arrange: the upstream rejects a token we believed was valid
        var answers = new Queue<HttpResponseMessage>([new HttpResponseMessage(HttpStatusCode.Unauthorized), Json("[]")]);
        var (client, stub) = CreateClient(_ => answers.Dequeue());

        // Act
        using var response = await client.GetAsync("products");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([FirstToken], _tokenProvider.InvalidatedTokens); // the token that actually failed, not "whatever is cached"
        Assert.Equal($"Bearer {FirstToken}", AuthorizationOf(stub.Requests[0]));
        Assert.Equal($"Bearer {SecondToken}", AuthorizationOf(stub.Requests[1]));
    }

    [Fact]
    public async Task Unauthorized_ReplaysThePostBodyOnTheRetry()
    {
        // Arrange
        var answers = new Queue<HttpResponseMessage>([new HttpResponseMessage(HttpStatusCode.Unauthorized), Json("{}", HttpStatusCode.Created)]);
        var (client, stub) = CreateClient(_ => answers.Dequeue());
        using var content = new StringContent(RequestBody, System.Text.Encoding.UTF8, "application/json");

        // Act
        using var response = await client.PostAsync("products", content);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal([RequestBody, RequestBody], stub.CapturedBodies);
        Assert.Equal(new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" }, stub.Requests[1].Content!.Headers.ContentType);
    }

    [Fact]
    public async Task Forbidden_IsNotTreatedAsAnExpiredToken()
    {
        // Arrange: 403 means the identity is known and not allowed; a new token would change nothing
        var (client, stub) = CreateClient(_ => Status(HttpStatusCode.Forbidden));

        // Act
        using var response = await client.GetAsync("products");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(stub.Requests);
        Assert.Equal(0, _tokenProvider.InvalidateCount);
    }

    [Fact]
    public async Task Unauthorized_ReplaysTheRequestHeadersOnTheRetry()
    {
        // Arrange
        var answers = new Queue<HttpResponseMessage>([Status(HttpStatusCode.Unauthorized), Json("[]")]);
        var (client, stub) = CreateClient(_ => answers.Dequeue());
        using var request = new HttpRequestMessage(HttpMethod.Get, "products");
        request.Headers.Add("X-Correlation-Id", "abc-123");

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(["abc-123"], stub.Requests[1].Headers.GetValues("X-Correlation-Id"));
    }

    [Fact]
    public async Task SecondUnauthorized_IsReturnedToTheCallerInsteadOfLooping()
    {
        // Arrange: a genuinely rejected identity must surface, not spin
        var (client, stub) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        // Act
        using var response = await client.GetAsync("products");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, stub.Requests.Count);
        Assert.Equal(1, _tokenProvider.InvalidateCount);
    }
}
