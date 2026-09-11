using CSharpApp.Application.Products;
using CSharpApp.Core.Dtos;

namespace CSharpApp.UnitTests.Application;

public class CreateProductCommandValidatorTests
{
    private readonly CreateProductCommandValidator _validator = new();
    private static readonly string[] ValidImages = ["https://img.example/1.png"];

    private static CreateProductCommand Command(
        string title = "Title", decimal price = 10, string description = "Description", int categoryId = 1, string[]? images = null)
        => new(new CreateProductRequest(title, price, description, categoryId, images ?? ValidImages));

    [Fact]
    public void ValidCommand_Passes()
    {
        // Arrange
        var command = Command();

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingTitle_Fails(string title)
    {
        // Arrange, Act
        var result = _validator.Validate(Command(title: title));

        // Assert
        Assert.Contains(result.Errors, e => e.PropertyName == "title");
    }

    [Fact]
    public void OverlongTitle_Fails()
    {
        // Arrange, Act
        var result = _validator.Validate(Command(title: new string('t', 201)));

        // Assert
        Assert.Contains(result.Errors, e => e.PropertyName == "title");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositivePrice_Fails(decimal price)
    {
        // Arrange, Act
        var result = _validator.Validate(Command(price: price));

        // Assert
        Assert.Contains(result.Errors, e => e.PropertyName == "price");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveCategoryId_Fails(int categoryId)
    {
        // Arrange, Act
        var result = _validator.Validate(Command(categoryId: categoryId));

        // Assert
        Assert.Contains(result.Errors, e => e.PropertyName == "categoryId");
    }

    [Fact]
    public void MissingDescription_Fails()
    {
        // Arrange, Act
        var result = _validator.Validate(Command(description: ""));

        // Assert
        Assert.Contains(result.Errors, e => e.PropertyName == "description");
    }

    [Fact]
    public void AbsentImages_FailsCleanlyInsteadOfThrowing()
    {
        // Arrange: a body without the "images" key deserializes to null, and the rules after NotEmpty still run
        var command = new CreateProductCommand(new CreateProductRequest("Title", 10, "Description", 1, null!));

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.Equal("images", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void OverlongImageUrl_FailsAndNamesTheOffendingElement()
    {
        // Arrange: an unbounded URL would be forwarded to the upstream as-is
        var command = Command(images: ["https://img.example/ok.png", $"https://img.example/{new string('x', 2100)}.png"]);

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.Equal("images[1]", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void NoImages_Fails()
    {
        // Arrange, Act
        var result = _validator.Validate(Command(images: []));

        // Assert
        Assert.Contains(result.Errors, e => e.PropertyName == "images");
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("/relative/path.png")]
    [InlineData("ftp://files.example/1.png")] // absolute, but a consumer rendering the product cannot use it
    public void ImageThatIsNotAnAbsoluteUrl_Fails(string image)
    {
        // Arrange: the upstream stores these verbatim and clients render them, so a relative path is useless
        var command = Command(images: [image]);

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
    }
}

public class GetProductsQueryValidatorTests
{
    private readonly GetProductsQueryValidator _validator = new();

    [Fact]
    public void NoPaging_Passes()
    {
        // Arrange, Act
        var result = _validator.Validate(new GetProductsQuery(null, null));

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(100, 250)]
    public void ValidPaging_Passes(int limit, int offset)
    {
        // Arrange, Act
        var result = _validator.Validate(new GetProductsQuery(limit, offset));

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(101, 0)]
    [InlineData(10, -1)]
    public void PagingOutOfRange_Fails(int limit, int offset)
    {
        // Arrange, Act
        var result = _validator.Validate(new GetProductsQuery(limit, offset));

        // Assert
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(10, null)]
    [InlineData(null, 10)]
    public void HalfSpecifiedPaging_Fails(int? limit, int? offset)
    {
        // Arrange: the upstream ignores one without the other, which would silently return an unpaged list
        var query = new GetProductsQuery(limit, offset);

        // Act
        var result = _validator.Validate(query);

        // Assert
        Assert.False(result.IsValid);
    }
}

public class GetProductByIdQueryValidatorTests
{
    private readonly GetProductByIdQueryValidator _validator = new();

    [Fact]
    public void PositiveId_Passes()
    {
        // Arrange, Act
        var result = _validator.Validate(new GetProductByIdQuery(1));

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveId_Fails(int id)
    {
        // Arrange: rejected here so a nonsensical id never spends an upstream call
        var query = new GetProductByIdQuery(id);

        // Act
        var result = _validator.Validate(query);

        // Assert
        Assert.False(result.IsValid);
    }
}
