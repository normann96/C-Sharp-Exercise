namespace CSharpApp.Infrastructure.Http;

public sealed class AccessTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<RestApiSettings> options,
    ILogger<AccessTokenProvider> logger,
    TimeProvider timeProvider) : ITokenProvider, IDisposable
{
    /// <summary>Stop handing out a token this long before it expires; the 401 refresh is the backstop for the rest.</summary>
    private static readonly TimeSpan ExpirySafetyWindow = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan AssumedLifetime = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim _loginGate = new(1, 1);
    private readonly string _loginPath = options.Value.Auth.AsRelativePath();

    private CachedToken? _cached;

    public async ValueTask<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (Usable(Volatile.Read(ref _cached)) is { } cached)
        {
            return cached.Token;
        }

        // Single flight: a cold start with N concurrent requests must produce one login, not N.
        await _loginGate.WaitAsync(ct);
        try
        {
            if (Usable(Volatile.Read(ref _cached)) is { } acquiredWhileWaiting)
            {
                return acquiredWhileWaiting.Token;
            }

            var token = await LogInAsync(ct);
            Volatile.Write(ref _cached, token);
            return token.Token;
        }
        finally
        {
            _loginGate.Release();
        }
    }

    public void Invalidate(string staleToken)
    {
        // Compare and swap: when several in-flight requests are rejected at once, only the first clears the
        // cache and the rest keep the token a concurrent refresh has already installed.
        var cached = Volatile.Read(ref _cached);
        if (cached is not null && cached.Token == staleToken)
        {
            Interlocked.CompareExchange(ref _cached, null, cached);
        }
    }

    public void Dispose() => _loginGate.Dispose();

    private CachedToken? Usable(CachedToken? token)
        => token is not null && timeProvider.GetUtcNow() < token.ExpiresAt - ExpirySafetyWindow ? token : null;

    private async Task<CachedToken> LogInAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var client = httpClientFactory.CreateClient(Configuration.HttpConfiguration.PlatziAuth);
        var credentials = new LoginRequest(settings.Username!, settings.Password!);

        using var response = await client.PostAsJsonAsync(_loginPath, credentials, PlatziJsonContext.Default.LoginRequest, ct);
        if (!response.IsSuccessStatusCode)
        {
            // The request carried credentials, so nothing from this exchange is quoted back.
            throw new HttpRequestException(HttpRequestError.UserAuthenticationError,
                $"Upstream auth endpoint rejected the login with {(int)response.StatusCode}.", null, response.StatusCode);
        }

        var auth = await response.Content.ReadFromJsonAsync(PlatziJsonContext.Default.AuthTokenResponse, ct)
                   ?? throw new HttpRequestException("Upstream auth endpoint returned an empty body.");

        var now = timeProvider.GetUtcNow();
        var expiresAt = JwtExpiry.TryGetExpiryUtc(auth.AccessToken) ?? now.Add(AssumedLifetime);
        if (expiresAt - now <= ExpirySafetyWindow)
        {
            // Caching it anyway: without this the cache would never hand it out and every request would log in.
            logger.LogWarning("Upstream access token expires at {TokenExpiresAt:O}, within the safety window; caching it regardless", expiresAt);
        }
        else
        {
            logger.LogInformation("Obtained an upstream access token valid until {TokenExpiresAt:O}", expiresAt);
        }

        return new CachedToken(auth.AccessToken, expiresAt);
    }

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAt);
}
