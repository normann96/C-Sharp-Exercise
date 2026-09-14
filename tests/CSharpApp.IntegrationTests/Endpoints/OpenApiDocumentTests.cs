using CSharpApp.IntegrationTests.Common;

namespace CSharpApp.IntegrationTests.Endpoints;

public sealed class OpenApiDocumentTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Document_ListsTheVersionedPaths_WithTheVersionSubstituted()
    {
        // Arrange
        var url = "/openapi/v1.json";

        // Act
        var response = await Client.GetAsync(url);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await ReadJsonAsync(response);
        var paths = document.GetProperty("paths").EnumerateObject().Select(path => path.Name).ToArray();
        Assert.Contains("/api/v1/products", paths);
        Assert.Contains("/api/v1/products/{id}", paths);
        Assert.Contains("/api/v1/categories/{id}", paths);
        Assert.DoesNotContain(paths, path => path.Contains("{version"));
    }
}
