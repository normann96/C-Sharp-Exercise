using System.Net;
using CSharpApp.Core.Settings;
using CSharpApp.Infrastructure.Http;
using CSharpApp.UnitTests.Common;
using CSharpApp.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CSharpApp.UnitTests.Http;

public class AccessTokenProviderTests : HttpTestBase
{
    private const string Password = "s3cret-value";

    private readonly IOptions<RestApiSettings> _settings = Options.Create(new RestApiSettings
    {
        BaseUrl = BaseUrl, Products = "products", Categories = "/categories",
        Auth = "/auth/login", Username = "john@mail.com", Password = Password
    });

    private readonly CapturingLogger<AccessTokenProvider> _logger = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    private (AccessTokenProvider Provider, StubHttpMessageHandler Stub) CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var (http, stub) = StubbedHttpClient(responder);
        return (new AccessTokenProvider(new StubHttpClientFactory(http), _settings, _logger, _clock), stub);
    }

    private static HttpResponseMessage LoginResponse(string token)
        => Json($$"""{"access_token":"{{token}}","refresh_token":"r"}""", HttpStatusCode.Created);

    private int _issued;

    // Distinct payloads per login: two tokens minted in the same second would otherwise be identical strings.
    private string LongLivedToken() => TokenExpiringIn(TimeSpan.FromHours(1));

    private string TokenExpiringIn(TimeSpan lifetime)
        => TestJwt.WithPayload(new { sub = ++_issued, exp = _clock.GetUtcNow().Add(lifetime).ToUnixTimeSeconds() });

    [Fact]
    public async Task ATokenIsReusedUntilItsSafetyWindowOpens_AndRefreshedAfterIt()
    {
        // Arrange: a token good for fifteen minutes, on a clock the test controls
        var (provider, stub) = CreateProvider(_ => LoginResponse(TokenExpiringIn(TimeSpan.FromMinutes(15))));
        using var _ = provider;
        var first = await provider.GetAccessTokenAsync(CancellationToken.None);

        // Act: stop just short of the sixty-second window, then step over it
        _clock.Advance(TimeSpan.FromMinutes(13));
        var reused = await provider.GetAccessTokenAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(2));
        var refreshed = await provider.GetAccessTokenAsync(CancellationToken.None);

        // Assert
        Assert.Equal(first, reused);
        Assert.NotEqual(first, refreshed);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task ConcurrentColdStart_PerformsExactlyOneLogin()
    {
        // Arrange: sixteen callers race for a token that nobody has yet
        var (provider, stub) = CreateProvider(_ => LoginResponse(LongLivedToken()));
        using var _ = provider;

        // Act
        var tokens = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Run(async () => await provider.GetAccessTokenAsync(CancellationToken.None))));

        // Assert
        Assert.Single(stub.Requests);
        Assert.Single(tokens.Distinct());
    }

    [Fact]
    public async Task CachedToken_IsReusedWithoutLoggingInAgain()
    {
        // Arrange
        var (provider, stub) = CreateProvider(_ => LoginResponse(LongLivedToken()));
        using var _ = provider;

        // Act
        var first = await provider.GetAccessTokenAsync(CancellationToken.None);
        var second = await provider.GetAccessTokenAsync(CancellationToken.None);

        // Assert
        Assert.Equal(first, second);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task TokenInsideTheSafetyWindow_TriggersANewLogin()
    {
        // Arrange: the first token is still technically valid but expires in 30s, inside the safety window
        var tokens = new Queue<string>([TokenExpiringIn(TimeSpan.FromSeconds(30)), LongLivedToken()]);
        var (provider, stub) = CreateProvider(_ => LoginResponse(tokens.Dequeue()));
        using var _ = provider;

        // Act
        var first = await provider.GetAccessTokenAsync(CancellationToken.None);
        var second = await provider.GetAccessTokenAsync(CancellationToken.None);

        // Assert
        Assert.NotEqual(first, second);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task Invalidate_ForcesANewLogin()
    {
        // Arrange
        var (provider, stub) = CreateProvider(_ => LoginResponse(LongLivedToken()));
        using var _ = provider;
        var token = await provider.GetAccessTokenAsync(CancellationToken.None);

        // Act
        provider.Invalidate(token);
        await provider.GetAccessTokenAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task Invalidate_KeepsATokenThatAConcurrentRefreshAlreadyReplaced()
    {
        // Arrange: several in-flight requests are rejected at once and all report the same stale token
        var (provider, stub) = CreateProvider(_ => LoginResponse(LongLivedToken()));
        using var _ = provider;
        var stale = await provider.GetAccessTokenAsync(CancellationToken.None);
        provider.Invalidate(stale);
        var refreshed = await provider.GetAccessTokenAsync(CancellationToken.None);

        // Act: a late arrival reports the token that is no longer cached
        provider.Invalidate(stale);
        var afterLateInvalidation = await provider.GetAccessTokenAsync(CancellationToken.None);

        // Assert
        Assert.Equal(refreshed, afterLateInvalidation);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task CancelledCaller_DoesNotBreakTheProviderForTheNextOne()
    {
        // Arrange: the gate is taken before the try block, so a cancelled wait must not over-release it
        var (provider, stub) = CreateProvider(_ => LoginResponse(LongLivedToken()));
        using var _ = provider;

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetAccessTokenAsync(new CancellationToken(canceled: true)).AsTask());
        var token = await provider.GetAccessTokenAsync(CancellationToken.None);

        // Assert
        Assert.NotEmpty(token);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task FailedLogin_LeavesTheProviderUsableForTheNextCaller()
    {
        // Arrange: the first login is rejected, the second succeeds
        var answers = new Queue<HttpResponseMessage>([Status(HttpStatusCode.Unauthorized), LoginResponse(LongLivedToken())]);
        var (provider, stub) = CreateProvider(_ => answers.Dequeue());
        using var _ = provider;

        // Act
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetAccessTokenAsync(CancellationToken.None).AsTask());
        var token = await provider.GetAccessTokenAsync(CancellationToken.None);

        // Assert
        Assert.NotEmpty(token);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task TokenWithoutAnExpiryClaim_IsStillUsable()
    {
        // Arrange: a token the provider cannot date falls back to a conservative assumed lifetime
        var (provider, stub) = CreateProvider(_ => LoginResponse(TestJwt.WithPayload(new { sub = 1 })));
        using var _ = provider;

        // Act
        var first = await provider.GetAccessTokenAsync(CancellationToken.None);
        var second = await provider.GetAccessTokenAsync(CancellationToken.None);

        // Assert
        Assert.Equal(first, second);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task ShortLivedToken_IsCachedAnywayAndWarnsInsteadOfLoggingInPerRequest()
    {
        // Arrange: a token that expires inside the safety window would otherwise never be handed out
        var (provider, stub) = CreateProvider(_ => LoginResponse(TokenExpiringIn(TimeSpan.FromSeconds(10))));
        using var _ = provider;

        // Act
        await provider.GetAccessTokenAsync(CancellationToken.None);

        // Assert
        Assert.Single(stub.Requests);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task LoginRequest_PostsTheConfiguredCredentialsToTheConfiguredPath()
    {
        // Arrange
        var (provider, stub) = CreateProvider(_ => LoginResponse(LongLivedToken()));
        using var _ = provider;

        // Act
        await provider.GetAccessTokenAsync(CancellationToken.None);

        // Assert
        Assert.Equal(HttpMethod.Post, stub.Requests[0].Method);
        Assert.Equal("https://api.escuelajs.co/api/v1/auth/login", stub.Requests[0].RequestUri!.ToString());
        Assert.Contains("\"email\":\"john@mail.com\"", stub.CapturedBodies[0]);
    }

    [Fact]
    public async Task RejectedLogin_ThrowsWithoutQuotingTheResponseBody()
    {
        // Arrange: the request carried credentials, so nothing from that exchange belongs in an exception message
        var (provider, _) = CreateProvider(_ => Json($$"""{"message":"{{Password}}"}""", HttpStatusCode.Unauthorized));
        using var __ = provider;

        // Act
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetAccessTokenAsync(CancellationToken.None).AsTask());

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.DoesNotContain(Password, exception.Message);
    }

    [Fact]
    public async Task AccessToken_AndPassword_AreNeverLogged()
    {
        // Arrange
        var token = LongLivedToken();
        var (provider, _) = CreateProvider(_ => LoginResponse(token));
        using var __ = provider;

        // Act
        await provider.GetAccessTokenAsync(CancellationToken.None);

        // Assert
        Assert.NotEmpty(_logger.Entries);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Message.Contains(token) || entry.Message.Contains(Password));
    }
}
