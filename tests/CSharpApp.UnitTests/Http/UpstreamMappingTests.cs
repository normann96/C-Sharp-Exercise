using CSharpApp.Core.Dtos;
using CSharpApp.Infrastructure.Http.Contracts;
using CSharpApp.Infrastructure.Mapping;
using Mapster;

namespace CSharpApp.UnitTests.Http;

public class UpstreamMappingTests
{
    private static PlatziProduct UpstreamProduct() => new()
    {
        Id = 7, Title = "Shirt", Slug = "shirt", Price = 12.5m, Description = "d",
        Images = ["https://img.example/1.png", "https://img.example/2.png"],
        CreationAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 1, 3, 4, 5, 6, DateTimeKind.Utc),
        Category = new PlatziCategory { Id = 1, Name = "Clothes", Slug = "clothes", Image = "https://img.example/c.png" },
    };

    [Fact]
    public void EveryPublishedMemberHasAnUpstreamSource()
    {
        // Arrange: the guard that convention-based mapping needs, kept here rather than at startup because the
        // configuration is the same in every environment and a test fails earlier and cheaper than a boot does
        var strict = new TypeAdapterConfig { RequireDestinationMemberSource = true };
        strict.Scan(typeof(UpstreamMappingRegister).Assembly);

        // Act
        var failure = Record.Exception(() => strict.Compile());

        // Assert
        Assert.Null(failure);
    }

    [Fact]
    public void APublishedMemberWithNoUpstreamSource_IsCaughtByThatGuard()
    {
        // Arrange: proves the test above would actually fail rather than pass vacuously
        var strict = new TypeAdapterConfig { RequireDestinationMemberSource = true };
        strict.NewConfig<SourceWithoutTheField, DestinationWithAnExtraField>();

        // Act
        var failure = Record.Exception(() => strict.Compile());

        // Assert
        Assert.NotNull(failure);
        Assert.Contains(nameof(DestinationWithAnExtraField.Unmapped), failure.ToString());
    }

    [Fact]
    public void EveryProductFieldSurvivesTheBoundary()
    {
        // Arrange
        var upstream = UpstreamProduct();

        // Act
        var published = upstream.Adapt<Product>();

        // Assert
        Assert.Equal(upstream.Id, published.Id);
        Assert.Equal(upstream.Title, published.Title);
        Assert.Equal(upstream.Slug, published.Slug);
        Assert.Equal(upstream.Price, published.Price);
        Assert.Equal(upstream.Description, published.Description);
        Assert.Equal(upstream.Images, published.Images);
        Assert.Equal(upstream.CreationAt, published.CreationAt);
        Assert.Equal(upstream.UpdatedAt, published.UpdatedAt);
    }

    [Fact]
    public void TheNestedCategoryIsMappedRatherThanShared()
    {
        // Arrange: a shared reference would let a change to one side reach the other
        var upstream = UpstreamProduct();

        // Act
        var published = upstream.Adapt<Product>();

        // Assert
        Assert.Equal("Clothes", published.Category?.Name);
        Assert.Equal("clothes", published.Category?.Slug);
        Assert.NotSame(upstream.Category, published.Category);
    }

    [Fact]
    public void AnAbsentUpstreamFieldStaysAbsent()
    {
        // Arrange: the upstream may omit anything, and inventing a value here would be a lie
        var upstream = new PlatziProduct { Id = 1 };

        // Act
        var published = upstream.Adapt<Product>();

        // Assert
        Assert.Null(published.Title);
        Assert.Null(published.Price);
        Assert.Null(published.Category);
        Assert.Empty(published.Images);
    }

    [Fact]
    public void AListMapsItemByItem()
    {
        // Arrange
        var upstream = new List<PlatziCategory> { new() { Id = 1, Name = "One" }, new() { Id = 2, Name = "Two" } };

        // Act
        var published = upstream.Adapt<List<Category>>();

        // Assert
        Assert.Equal([1, 2], published.Select(category => category.Id));
        Assert.Equal(["One", "Two"], published.Select(category => category.Name));
    }

    private sealed class SourceWithoutTheField
    {
        public int Id { get; set; }
    }

    private sealed class DestinationWithAnExtraField
    {
        public int Id { get; set; }
        public string? Unmapped { get; set; }
    }
}
