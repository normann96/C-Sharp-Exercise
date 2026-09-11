using CSharpApp.Infrastructure.Http;
using CSharpApp.UnitTests.TestDoubles;

namespace CSharpApp.UnitTests.Http;

public class JwtExpiryTests
{
    [Fact]
    public void ReadsTheExpiryClaim()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var token = TestJwt.WithExpiry(expiresAt);

        // Act
        var parsed = JwtExpiry.TryGetExpiryUtc(token);

        // Assert
        Assert.Equal(expiresAt.ToUnixTimeSeconds(), parsed!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void PayloadWithoutExpiry_ReturnsNull()
    {
        // Arrange
        var token = TestJwt.WithPayload(new { sub = 1 });

        // Act
        var parsed = JwtExpiry.TryGetExpiryUtc(token);

        // Assert
        Assert.Null(parsed);
    }

    [Fact]
    public void NonNumericExpiry_ReturnsNull()
    {
        // Arrange
        var token = TestJwt.WithPayload(new { exp = "soon" });

        // Act
        var parsed = JwtExpiry.TryGetExpiryUtc(token);

        // Assert
        Assert.Null(parsed);
    }

    [Fact]
    public void PayloadThatIsNotAnObject_ReturnsNull()
    {
        // Arrange: an opaque token whose middle segment decodes to a bare JSON number
        var token = TestJwt.WithPayload(42);

        // Act
        var parsed = JwtExpiry.TryGetExpiryUtc(token);

        // Assert
        Assert.Null(parsed);
    }

    [Theory]
    [InlineData(1795000000000L)] // a real-world issuer bug: "exp" in milliseconds
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void ExpiryOutsideTheRepresentableRange_ReturnsNull(long exp)
    {
        // Arrange
        var token = TestJwt.WithPayload(new { exp });

        // Act
        var parsed = JwtExpiry.TryGetExpiryUtc(token);

        // Assert
        Assert.Null(parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("two.parts")]
    [InlineData("header.!!!not-base64!!!.signature")]
    [InlineData("header.eyJub3QiOiAianNvbiI.signature")]
    public void MalformedToken_ReturnsNull(string token)
    {
        // Act
        var parsed = JwtExpiry.TryGetExpiryUtc(token);

        // Assert
        Assert.Null(parsed);
    }
}
