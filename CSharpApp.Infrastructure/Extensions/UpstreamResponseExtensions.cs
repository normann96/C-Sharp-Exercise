namespace CSharpApp.Infrastructure.Extensions;

internal static class UpstreamResponseExtensions
{
    // The sandbox answers 400 with this error name (not 404) when an id does not exist.
    private const string NotFoundMarker = "EntityNotFoundError";
    private const int MaxQuotedBodyLength = 500;

    public static async Task<bool> IsUpstreamNotFoundAsync(this HttpResponseMessage response, CancellationToken ct)
        => response.StatusCode == HttpStatusCode.NotFound
           || (response.StatusCode == HttpStatusCode.BadRequest
               && (await response.Content.ReadAsStringAsync(ct)).Contains(NotFoundMarker, StringComparison.Ordinal));

    /// <summary>
    /// Throws <see cref="UpstreamRejectedRequestException"/> for a 4xx that is about the request's content, which
    /// the caller can act on. Credential and rate-limit answers are not the caller's business and fall through.
    /// </summary>
    public static async Task ThrowIfRejectedAsync(this HttpResponseMessage response, CancellationToken ct)
    {
        var status = response.StatusCode;
        var aboutTheContent = (int)status is >= 400 and < 500
                              && status is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests);
        if (aboutTheContent)
        {
            throw new UpstreamRejectedRequestException(await DescribeAsync(response, ct), status);
        }
    }

    /// <summary>Throws an <see cref="HttpRequestException"/> carrying the status code and at most 500 characters of the upstream body.</summary>
    public static async Task EnsureUpstreamSuccessAsync(this HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await DescribeAsync(response, ct), null, response.StatusCode);
        }
    }

    private static async Task<string> DescribeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        var quoted = body.Length <= MaxQuotedBodyLength ? body : body[..MaxQuotedBodyLength];
        return $"Upstream API rejected the request with {(int)response.StatusCode}: {quoted}";
    }
}
