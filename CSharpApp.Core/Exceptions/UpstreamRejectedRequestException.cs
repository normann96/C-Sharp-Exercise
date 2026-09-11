using System.Net;

namespace CSharpApp.Core.Exceptions;

/// <summary>
/// The upstream refused a well-formed request because of its content, typically a reference it does not know.
/// The caller can act on it, so it must not be reported as a gateway failure.
/// </summary>
public sealed class UpstreamRejectedRequestException(string message, HttpStatusCode statusCode)
    : HttpRequestException(message, null, statusCode);
