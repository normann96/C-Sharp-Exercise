using System.Text;
using System.Text.Json;

namespace CSharpApp.UnitTests.TestDoubles;

/// <summary>Builds structurally valid (unsigned) JWTs; only the payload matters to the code under test.</summary>
public static class TestJwt
{
    public static string WithExpiry(DateTimeOffset expiresAt) => WithPayload(new { sub = 1, exp = expiresAt.ToUnixTimeSeconds() });

    public static string WithPayload(object payload)
        => $"{Base64Url("""{"alg":"HS256","typ":"JWT"}""")}.{Base64Url(JsonSerializer.Serialize(payload))}.signature";

    private static string Base64Url(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
