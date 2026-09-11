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

    /// <summary>Throws an <see cref="HttpRequestException"/> carrying the status code and at most 500 characters of the upstream body.</summary>
    public static async Task EnsureUpstreamSuccessAsync(this HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var quoted = body.Length <= MaxQuotedBodyLength ? body : body[..MaxQuotedBodyLength];
        throw new HttpRequestException($"Upstream API rejected the request with {(int)response.StatusCode}: {quoted}", null, response.StatusCode);
    }
}
