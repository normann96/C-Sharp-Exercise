namespace CSharpApp.Core.Extensions;

public static class UrlExtensions
{
    /// <summary>
    /// True for an absolute http(s) URL. Used wherever a value is handed to an HTTP client or rendered by a
    /// consumer, where a scheme-less or non-http value is useless.
    /// </summary>
    public static bool IsAbsoluteHttpUrl(this string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
