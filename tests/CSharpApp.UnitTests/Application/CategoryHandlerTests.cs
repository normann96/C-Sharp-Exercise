using CSharpApp.Application.Categories;
using CSharpApp.Core.Dtos;
using CSharpApp.UnitTests.TestDoubles;

namespace CSharpApp.UnitTests.Application;

public class CategoryHandlerTests
{
    private readonly FakePlatziStoreClient _client = new()
    {
        Categories = [new Category { Id = 1 }, new Category { Id = 2 }],
        Category = new Category { Id = 42 }
    };

    [Fact]
    public async Task GetCategories_ReturnsWhatTheClientReturns()
    {
        // Arrange
        var handler = new GetCategoriesQueryHandler(_client);

        // Act
        var categories = await handler.Handle(new GetCategoriesQuery(), CancellationToken.None);

        // Assert
        Assert.Equal(2, categories.Count);
    }

    [Fact]
    public async Task GetCategoryById_ForwardsTheId()
    {
        // Arrange
        var handler = new GetCategoryByIdQueryHandler(_client);

        // Act
        var category = await handler.Handle(new GetCategoryByIdQuery(42), CancellationToken.None);

        // Assert
        Assert.Equal(42, _client.RequestedCategoryId);
        Assert.Equal(42, category!.Id);
    }

    [Fact]
    public async Task CreateCategory_ForwardsTheRequestUnchanged()
    {
        // Arrange
        var request = new CreateCategoryRequest("Books", "https://img.example/books.png");
        var handler = new CreateCategoryCommandHandler(_client);

        // Act
        await handler.Handle(new CreateCategoryCommand(request), CancellationToken.None);

        // Assert
        Assert.Same(request, _client.CreatedCategory);
    }
}
