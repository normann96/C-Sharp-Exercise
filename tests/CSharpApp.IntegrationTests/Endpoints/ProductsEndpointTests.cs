using CSharpApp.Core.Dtos;
using CSharpApp.IntegrationTests.Common;
using CSharpApp.IntegrationTests.TestDoubles;

namespace CSharpApp.IntegrationTests.Endpoints;

public sealed class ProductsEndpointTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private const string ProductsUpstreamPath = FakePlatziHandler.ApiPrefix + "/products";

    private static readonly object ValidProduct = new
    {
        title = "Integration", price = 10.5, description = "d", categoryId = 1, images = new[] { "https://img.example/1.png" },
    };

    [Fact]
    public async Task GetProducts_ReturnsTheUpstreamList_WithABearerTokenAttachedUpstream()
    {
        // Arrange
        var url = ProductsRoute;

        // Act
        var response = await Client.GetAsync(url);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var products = await response.Content.ReadFromJsonAsync<List<Product>>();
        Assert.Equal(2, products!.Count);
        Assert.Equal("Fake product", products[0].Title);
        var upstream = Fake.LastStoreRequest;
        Assert.Equal(ProductsUpstreamPath, upstream.PathAndQuery);
        Assert.StartsWith("Bearer ", upstream.Authorization);
    }

    [Fact]
    public async Task GetProducts_PassesThePagingThrough()
    {
        // Arrange
        var url = $"{ProductsRoute}?limit=5&offset=10";

        // Act
        var response = await Client.GetAsync(url);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"{ProductsUpstreamPath}?limit=5&offset=10", Fake.LastStoreRequest.PathAndQuery);
    }

    [Fact]
    public async Task GetProducts_WithHalfThePaging_Returns400_BeforeAnyUpstreamCall()
    {
        // Arrange
        var url = $"{ProductsRoute}?limit=5";
        var upstreamCalls = Fake.StoreRequests.Count;

        // Act
        var response = await Client.GetAsync(url);

        // Assert
        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Contains("paging", ErrorKeys(problem));
        Assert.Equal(upstreamCalls, Fake.StoreRequests.Count);
    }

    [Fact]
    public async Task GetProduct_Known_Returns200()
    {
        // Arrange
        var url = $"{ProductsRoute}/1";

        // Act
        var response = await Client.GetAsync(url);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var product = await response.Content.ReadFromJsonAsync<Product>();
        Assert.Equal(1, product!.Id);
        Assert.Equal("Fake product", product.Title);
    }

    [Fact]
    public async Task GetProduct_UnknownUpstream_Returns404Problem_WithoutTheUpstreamWording()
    {
        // Arrange
        var url = $"{ProductsRoute}/{FakePlatziHandler.UnknownId}";

        // Act
        var response = await Client.GetAsync(url);

        // Assert
        var problem = await AssertProblemAsync(response, HttpStatusCode.NotFound);
        Assert.DoesNotContain("EntityNotFoundError", problem.GetRawText());
    }

    [Fact]
    public async Task GetProduct_NonPositiveId_Returns400_WithTheFieldNamed()
    {
        // Arrange
        var url = $"{ProductsRoute}/0";

        // Act
        var response = await Client.GetAsync(url);

        // Assert
        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(["id"], ErrorKeys(problem));
    }

    [Fact]
    public async Task CreateProduct_Valid_Returns201_WithLocation_AndSendsTheJsonNamesUpstream()
    {
        // Arrange
        var request = ValidProduct;

        // Act
        var response = await Client.PostAsJsonAsync(ProductsRoute, request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"{ProductsRoute}/{FakePlatziHandler.CreatedProductId}", response.Headers.Location?.AbsolutePath);
        var created = await response.Content.ReadFromJsonAsync<Product>();
        Assert.Equal("Integration", created!.Title);
        var upstream = Fake.LastStoreRequest;
        Assert.Equal(HttpMethod.Post, upstream.Method);
        Assert.Equal(1, ParseJson(upstream.Body).GetProperty("categoryId").GetInt32());
    }

    [Fact]
    public async Task CreateProduct_Invalid_Returns400_WithFieldErrors_AndNothingSentUpstream()
    {
        // Arrange
        var request = new { title = "", price = 0 };
        var upstreamCalls = Fake.StoreRequests.Count;

        // Act
        var response = await Client.PostAsJsonAsync(ProductsRoute, request);

        // Assert
        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        var errors = ErrorKeys(problem);
        Assert.Contains("title", errors);
        Assert.Contains("price", errors);
        Assert.Equal(upstreamCalls, Fake.StoreRequests.Count);
    }

    [Fact]
    public async Task CreateProduct_UpstreamRejectsTheContent_Returns422_WithoutTheUpstreamText()
    {
        // Arrange
        var request = new { title = "Integration", price = 10, description = "d", categoryId = FakePlatziHandler.UnknownCategoryId, images = new[] { "https://img.example/1.png" } };

        // Act
        var response = await Client.PostAsJsonAsync(ProductsRoute, request);

        // Assert
        var problem = await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity);
        Assert.DoesNotContain("Could not find", problem.GetRawText());
    }

    [Fact]
    public async Task MalformedJson_Returns400Problem()
    {
        // Arrange
        var request = new StringContent("{", System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await Client.PostAsync(ProductsRoute, request);

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TransientUpstreamFailure_IsHealedByARetry()
    {
        // Arrange
        var upstreamCalls = Fake.StoreRequests.Count;
        Fake.FailNextStoreCalls(1, HttpStatusCode.ServiceUnavailable);

        // Act
        var response = await Client.GetAsync(ProductsRoute);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(upstreamCalls + 2, Fake.StoreRequests.Count);
    }

    [Fact]
    public async Task PersistentUpstreamFailure_IsRetriedTheConfiguredTimes_ThenAnsweredAs502_WithoutTheUpstreamBody()
    {
        // Arrange
        var upstreamCalls = Fake.StoreRequests.Count;
        Fake.FailNextStoreCalls(UpstreamAttempts, HttpStatusCode.InternalServerError);

        // Act
        var response = await Client.GetAsync(ProductsRoute);

        // Assert
        var problem = await AssertProblemAsync(response, HttpStatusCode.BadGateway);
        Assert.DoesNotContain(FakePlatziHandler.UpstreamFailureBody, problem.GetRawText());
        Assert.Equal(upstreamCalls + UpstreamAttempts, Fake.StoreRequests.Count);
    }

    [Fact]
    public async Task UpstreamFailure_OnAWrite_IsNotRetried()
    {
        // Arrange: a retried create could duplicate the product
        var upstreamCalls = Fake.StoreRequests.Count;
        Fake.FailNextStoreCalls(1, HttpStatusCode.InternalServerError);

        // Act
        var response = await Client.PostAsJsonAsync(ProductsRoute, ValidProduct);

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.BadGateway);
        Assert.Equal(upstreamCalls + 1, Fake.StoreRequests.Count);
    }

    [Fact]
    public async Task UpstreamRateLimiting_IsAnsweredAs503_AfterTheConfiguredAttempts()
    {
        // Arrange
        var upstreamCalls = Fake.StoreRequests.Count;
        Fake.FailNextStoreCalls(UpstreamAttempts, HttpStatusCode.TooManyRequests);

        // Act
        var response = await Client.GetAsync(ProductsRoute);

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable);
        Assert.Equal(upstreamCalls + UpstreamAttempts, Fake.StoreRequests.Count);
    }
}
