using CSharpApp.Application.Products;
using CSharpApp.Core.Dtos;
using CSharpApp.UnitTests.TestDoubles;

namespace CSharpApp.UnitTests.Application;

public class ProductHandlerTests
{
    private readonly FakePlatziStoreClient _client = new()
    {
        Products = [new Product { Id = 1 }, new Product { Id = 2 }],
        Product = new Product { Id = 42 }
    };

    [Fact]
    public async Task GetProducts_ForwardsPagingInTheRightOrder()
    {
        // Arrange: limit and offset are both ints, so a swapped pair would be invisible without this
        var handler = new GetProductsQueryHandler(_client);

        // Act
        var products = await handler.Handle(new GetProductsQuery(Limit: 10, Offset: 20), CancellationToken.None);

        // Assert
        Assert.Equal((10, 20), _client.ProductsQuery);
        Assert.Equal(2, products.Count);
    }

    [Fact]
    public async Task GetProductById_ForwardsTheId()
    {
        // Arrange
        var handler = new GetProductByIdQueryHandler(_client);

        // Act
        var product = await handler.Handle(new GetProductByIdQuery(42), CancellationToken.None);

        // Assert
        Assert.Equal(42, _client.RequestedProductId);
        Assert.Equal(42, product!.Id);
    }

    [Fact]
    public async Task CreateProduct_ForwardsTheRequestUnchanged()
    {
        // Arrange
        var request = new CreateProductRequest("Title", 10, "Description", 1, ["https://img.example/1.png"]);
        var handler = new CreateProductCommandHandler(_client);

        // Act
        await handler.Handle(new CreateProductCommand(request), CancellationToken.None);

        // Assert
        Assert.Same(request, _client.CreatedProduct);
    }
}
