using System.Net;
using CSharpApp.Core.Dtos;
using CSharpApp.Core.Exceptions;
using CSharpApp.Core.Settings;
using CSharpApp.Infrastructure.Http;
using CSharpApp.UnitTests.Common;
using CSharpApp.UnitTests.TestDoubles;
using Microsoft.Extensions.Options;

namespace CSharpApp.UnitTests.Http;

public class PlatziStoreClientUrlTests : HttpTestBase
{
    private const string ProductJson = """{"id":42,"title":"New","price":10,"description":"d","images":["https://img.example/1.png"],"category":{"id":1,"name":"C"}}""";

    private readonly IOptions<RestApiSettings> _settings = Options.Create(new RestApiSettings
    {
        BaseUrl = BaseUrl, Products = "products", Categories = "/categories",
        Auth = "/auth/login", Username = "u", Password = "p"
    });

    private readonly CreateProductRequest _newProduct = new("New", 10, "d", 1, ["https://img.example/1.png"]);

    private (PlatziStoreClient Client, StubHttpMessageHandler Stub) CreateClient(Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        var (http, stub) = StubbedHttpClient(responder ?? (_ => Json("[]")));
        return (new PlatziStoreClient(http, _settings), stub);
    }

    [Fact]
    public async Task GetCategories_LeadingSlashInConfig_StaysUnderBasePath()
    {
        // Arrange
        var (client, stub) = CreateClient();

        // Act
        await client.GetCategoriesAsync(CancellationToken.None);

        // Assert
        Assert.Equal("https://api.escuelajs.co/api/v1/categories", stub.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetCategory_LeadingSlashInConfig_AppendsIdUnderBasePath()
    {
        // Arrange
        var (client, stub) = CreateClient(_ => Json("""{"id":7,"name":"Books"}"""));

        // Act
        await client.GetCategoryAsync(7, CancellationToken.None);

        // Assert
        Assert.Equal("https://api.escuelajs.co/api/v1/categories/7", stub.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetProducts_WithPaging_AppendsQuery()
    {
        // Arrange
        var (client, stub) = CreateClient();

        // Act
        await client.GetProductsAsync(limit: 10, offset: 0, CancellationToken.None);

        // Assert
        Assert.Equal("https://api.escuelajs.co/api/v1/products?limit=10&offset=0", stub.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetProducts_WithoutPaging_HasNoQuery()
    {
        // Arrange
        var (client, stub) = CreateClient();

        // Act
        await client.GetProductsAsync(null, null, CancellationToken.None);

        // Assert
        Assert.Equal("https://api.escuelajs.co/api/v1/products", stub.Requests[0].RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "")]
    [InlineData(HttpStatusCode.BadRequest, """{"name":"EntityNotFoundError","message":"Could not find any entity"}""")] // the sandbox's actual answer
    public async Task GetProduct_UpstreamSignalsUnknownId_ReturnsNull(HttpStatusCode status, string body)
    {
        // Arrange
        var (client, _) = CreateClient(_ => Json(body, status));

        // Act
        var product = await client.GetProductAsync(999999, CancellationToken.None);

        // Assert
        Assert.Null(product);
    }

    [Fact]
    public async Task GetProduct_UpstreamBadRequestWithoutNotFoundMarker_Throws()
    {
        // Arrange
        var (client, _) = CreateClient(_ => Json("""{"message":"something else entirely"}""", HttpStatusCode.BadRequest));

        // Act
        var act = () => client.GetProductAsync(1, CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(act);
    }

    [Fact]
    public async Task GetProduct_UpstreamServerError_Throws()
    {
        // Arrange
        var (client, _) = CreateClient(_ => Status(HttpStatusCode.InternalServerError));

        // Act
        var act = () => client.GetProductAsync(1, CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(act);
    }

    [Fact]
    public async Task CreateProduct_PostsJsonBodyToProducts()
    {
        // Arrange
        var (client, stub) = CreateClient(_ => Json(ProductJson, HttpStatusCode.Created));

        // Act
        var created = await client.CreateProductAsync(_newProduct, CancellationToken.None);

        // Assert
        Assert.Equal(HttpMethod.Post, stub.Requests[0].Method);
        Assert.Equal("https://api.escuelajs.co/api/v1/products", stub.Requests[0].RequestUri!.ToString());
        Assert.Contains("\"categoryId\":1", stub.CapturedBodies[0]);
        Assert.Contains("\"price\":10", stub.CapturedBodies[0]);
        Assert.Equal(42, created.Id);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task CreateProduct_UpstreamFailsForReasonsThatAreNotTheCallers_ThrowsAPlainRequestException(HttpStatusCode status)
    {
        // Arrange: credentials, rate limits and outages are the gateway's problem, not the request's content
        var (client, _) = CreateClient(_ => Status(status));

        // Act
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.CreateProductAsync(_newProduct, CancellationToken.None));

        // Assert
        Assert.Equal(status, exception.StatusCode);
    }

    [Fact]
    public async Task GetProducts_UpstreamServerError_Throws()
    {
        // Arrange: the list endpoints are buffered now and go through the same failure path as the rest
        var (client, _) = CreateClient(_ => Status(HttpStatusCode.BadGateway));

        // Act
        var act = () => client.GetProductsAsync(null, null, CancellationToken.None);

        // Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(act);
        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_UpstreamRejects_ThrowsWithStatusButWithoutFullBody()
    {
        // Arrange
        var hugeBody = new string('x', 5000);
        var (client, _) = CreateClient(_ => Json(hugeBody, HttpStatusCode.BadRequest));

        // Act
        var exception = await Assert.ThrowsAsync<UpstreamRejectedRequestException>(() => client.CreateProductAsync(_newProduct, CancellationToken.None));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.True(exception.Message.Length < 1000, "upstream bodies must be truncated in exceptions");
    }
}
