using System.Text.Json;

namespace CSharpApp.Infrastructure.Http;

public static class JwtExpiry
{
    // DateTimeOffset.FromUnixTimeSeconds throws outside this range; "exp" in milliseconds is a common issuer bug.
    private const long MinUnixSeconds = -62135596800;
    private const long MaxUnixSeconds = 253402300799;

    /// <summary>
    /// Reads the "exp" claim without validating the signature: the token is ours, we only need to know when to
    /// replace it. Returns null for anything unexpected - a malformed token must not take the auth flow down.
    /// </summary>
    public static DateTimeOffset? TryGetExpiryUtc(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

        try
        {
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));

            // Both TryGetProperty and TryGetInt64 throw rather than return false on an unexpected shape.
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("exp", out var expiry)
                   && expiry.ValueKind == JsonValueKind.Number
                   && expiry.TryGetInt64(out var secondsSinceEpoch)
                   && secondsSinceEpoch is >= MinUnixSeconds and <= MaxUnixSeconds
                ? DateTimeOffset.FromUnixTimeSeconds(secondsSinceEpoch)
                : null;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }
}
