using CSharpApp.Core.Dtos;
using CSharpApp.IntegrationTests.Common;
using CSharpApp.IntegrationTests.TestDoubles;

namespace CSharpApp.IntegrationTests.Endpoints;

public sealed class CategoriesEndpointTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task GetCategories_ReturnsTheUpstreamList()
    {
        // Arrange
        var url = CategoriesRoute;

        // Act
        var response = await Client.GetAsync(url);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categories = await response.Content.ReadFromJsonAsync<List<Category>>();
        var category = Assert.Single(categories!);
        Assert.Equal("Fake", category.Name);
        Assert.StartsWith("Bearer ", Fake.LastStoreRequest.Authorization);
    }

    [Fact]
    public async Task GetCategory_Known_Returns200()
    {
        // Arrange
        var url = $"{CategoriesRoute}/1";

        // Act
        var response = await Client.GetAsync(url);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var category = await response.Content.ReadFromJsonAsync<Category>();
        Assert.Equal(1, category!.Id);
    }

    [Fact]
    public async Task GetCategory_UnknownUpstream_Returns404Problem()
    {
        // Arrange
        var url = $"{CategoriesRoute}/{FakePlatziHandler.UnknownId}";

        // Act
        var response = await Client.GetAsync(url);

        // Assert
        var problem = await AssertProblemAsync(response, HttpStatusCode.NotFound);
        Assert.DoesNotContain("EntityNotFoundError", problem.GetRawText());
    }

    [Fact]
    public async Task GetCategory_NonPositiveId_Returns400_WithTheFieldNamed()
    {
        // Arrange
        var url = $"{CategoriesRoute}/-1";

        // Act
        var response = await Client.GetAsync(url);

        // Assert
        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(["id"], ErrorKeys(problem));
    }

    [Fact]
    public async Task CreateCategory_Valid_Returns201_WithLocation()
    {
        // Arrange
        var request = new { name = "Books", image = "https://img.example/b.png" };

        // Act
        var response = await Client.PostAsJsonAsync(CategoriesRoute, request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"{CategoriesRoute}/{FakePlatziHandler.CreatedCategoryId}", response.Headers.Location?.AbsolutePath);
        var created = await response.Content.ReadFromJsonAsync<Category>();
        Assert.Equal("Books", created!.Name);
    }

    [Fact]
    public async Task CreateCategory_Invalid_Returns400_WithFieldErrors_AndNothingSentUpstream()
    {
        // Arrange
        var request = new { name = "", image = "not a url" };
        var upstreamCalls = Fake.StoreRequests.Count;

        // Act
        var response = await Client.PostAsJsonAsync(CategoriesRoute, request);

        // Assert
        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(["image", "name"], ErrorKeys(problem).Order());
        Assert.Equal(upstreamCalls, Fake.StoreRequests.Count);
    }
}
