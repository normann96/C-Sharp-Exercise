using System.Text;

namespace CSharpApp.Core.Dtos;

public sealed record LoginRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password)
{
    // The compiler-generated ToString() prints every property; these two types must never print their secret.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Email = ").Append(Email);
        return true;
    }
}

public sealed record AuthTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken)
{
    private bool PrintMembers(StringBuilder builder) => false;
}
