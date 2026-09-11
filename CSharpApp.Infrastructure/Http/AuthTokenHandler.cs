using System.Net.Http.Headers;

namespace CSharpApp.Infrastructure.Http;

public sealed class AuthTokenHandler(ITokenProvider tokenProvider, ILogger<AuthTokenHandler> logger) : DelegatingHandler
{
    private const string Scheme = "Bearer";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        // Copied up front: sending consumes the request, and a 401 retry must replay the same body.
        using var retry = await CloneAsync(request, ct);

        var token = await tokenProvider.GetAccessTokenAsync(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue(Scheme, token);

        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        logger.LogWarning("Upstream rejected the access token for {RequestUri}; refreshing it and retrying once", request.RequestUri);
        tokenProvider.Invalidate(token);

        retry.Headers.Authorization = new AuthenticationHeaderValue(Scheme, await tokenProvider.GetAccessTokenAsync(ct));
        return await base.SendAsync(retry, ct);
    }

    // The synchronous path would bypass this handler's token entirely, so it is closed rather than left silent.
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken ct)
        => throw new NotSupportedException("Use the asynchronous API: the synchronous path cannot attach an access token.");

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in (IDictionary<string, object?>)request.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }

        if (request.Content is not null)
        {
            var body = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(ct));
            foreach (var header in request.Content.Headers)
            {
                body.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = body;
        }

        return clone;
    }
}
