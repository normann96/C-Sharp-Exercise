namespace CSharpApp.Core.Interfaces;

public interface ITokenProvider
{
    /// <summary>
    /// Returns a cached token, or acquires one. Acquisition is single-flight: concurrent callers share one login.
    /// Throws <see cref="HttpRequestException"/> when the upstream rejects the credentials or is unreachable.
    /// </summary>
    ValueTask<string> GetAccessTokenAsync(CancellationToken ct);

    /// <summary>
    /// Drops the cached token if it is still the one that failed, so a concurrent refresh is not thrown away.
    /// </summary>
    void Invalidate(string staleToken);
}
