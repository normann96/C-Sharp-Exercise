using System.Text.Json;
using CSharpApp.Infrastructure.Http;

namespace CSharpApp.UnitTests.Http;

public class PlatziJsonContextTests
{
    [Fact]
    public void Product_ImagesAreDeserialized()
    {
        // Arrange: the scaffold declared Images as a get-only list, which System.Text.Json silently skips on read
        const string json = """{"id":1,"title":"T","price":10,"description":"d","images":["https://img.example/1.png"],"category":{"id":1,"name":"C"}}""";

        // Act
        var product = JsonSerializer.Deserialize(json, PlatziJsonContext.Default.Product);

        // Assert
        Assert.NotNull(product);
        Assert.Equal("https://img.example/1.png", Assert.Single(product.Images));
        Assert.Equal("C", product.Category?.Name);
    }

    [Fact]
    public void Product_AndItsCategory_KeepTheUpstreamSlug()
    {
        // Arrange: a proxy that drops fields silently narrows the contract its clients see
        const string json = """{"id":1,"title":"T","slug":"t-shirt","price":10,"category":{"id":1,"name":"C","slug":"clothes"}}""";

        // Act
        var product = JsonSerializer.Deserialize(json, PlatziJsonContext.Default.Product);

        // Assert
        Assert.Equal("t-shirt", product!.Slug);
        Assert.Equal("clothes", product.Category?.Slug);
    }

    [Fact]
    public void Product_PriceMayBeFractional()
    {
        // Arrange: the upstream schema says "number" and the sandbox is publicly writable, so one 10.5 must not break the whole list
        const string json = """{"id":1,"title":"T","price":10.5}""";

        // Act
        var product = JsonSerializer.Deserialize(json, PlatziJsonContext.Default.Product);

        // Assert
        Assert.Equal(10.5m, product!.Price);
    }

    [Fact]
    public void AuthTokenResponse_UsesSnakeCaseWireNames()
    {
        // Arrange
        const string json = """{"access_token":"a","refresh_token":"r"}""";

        // Act
        var auth = JsonSerializer.Deserialize(json, PlatziJsonContext.Default.AuthTokenResponse);

        // Assert
        Assert.Equal("a", auth!.AccessToken);
        Assert.Equal("r", auth.RefreshToken);
    }
}
