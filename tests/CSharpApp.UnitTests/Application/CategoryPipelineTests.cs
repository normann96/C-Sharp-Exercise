using CSharpApp.Application.Categories;
using CSharpApp.Core.Dtos;
using CSharpApp.Core.Interfaces;
using CSharpApp.UnitTests.Common;
using CSharpApp.UnitTests.TestDoubles;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace CSharpApp.UnitTests.Application;

public class CategoryPipelineTests : ServiceProviderTestBase
{
    private readonly FakePlatziStoreClient _client = new()
    {
        Categories = [new Category { Id = 1 }],
        Category = new Category { Id = 7 }
    };

    private ISender Sender()
        => BuildProvider(configureServices: services => services.AddSingleton<IPlatziStoreClient>(_client))
            .GetRequiredService<ISender>();

    [Fact]
    public async Task ParameterlessQuery_ReachesItsHandlerThroughTheRealPipeline()
    {
        // Arrange: GetCategoriesQuery has no validator at all, which the behaviour must tolerate
        var sender = Sender();

        // Act
        var categories = await sender.Send(new GetCategoriesQuery());

        // Assert
        Assert.Single(categories);
    }

    [Fact]
    public async Task ValidQuery_ReachesTheUpstreamThroughTheRealPipeline()
    {
        // Arrange: proves the handler and its request type are resolvable from the container, not just newable
        var sender = Sender();

        // Act
        var category = await sender.Send(new GetCategoryByIdQuery(7));

        // Assert
        Assert.Equal(7, _client.RequestedCategoryId);
        Assert.Equal(7, category!.Id);
    }

    [Fact]
    public async Task InvalidCommand_IsRejectedBeforeTheUpstreamIsCalled()
    {
        // Arrange
        var sender = Sender();
        var command = new CreateCategoryCommand(new CreateCategoryRequest("", "nope"));

        // Act
        var act = () => sender.Send(command);

        // Assert
        await Assert.ThrowsAsync<ValidationException>(act);
        Assert.Null(_client.CreatedCategory);
    }
}
