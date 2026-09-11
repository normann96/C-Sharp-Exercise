using CSharpApp.Application.Categories;
using CSharpApp.Core.Dtos;

namespace CSharpApp.UnitTests.Application;

public class CreateCategoryCommandValidatorTests
{
    private readonly CreateCategoryCommandValidator _validator = new();

    private static CreateCategoryCommand Command(string name = "Books", string image = "https://img.example/books.png")
        => new(new CreateCategoryRequest(name, image));

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
    public void MissingName_Fails(string name)
    {
        // Arrange, Act
        var result = _validator.Validate(Command(name: name));

        // Assert
        Assert.Contains(result.Errors, e => e.PropertyName == "name");
    }

    [Fact]
    public void OverlongName_Fails()
    {
        // Arrange, Act
        var result = _validator.Validate(Command(name: new string('n', 101)));

        // Assert
        Assert.Contains(result.Errors, e => e.PropertyName == "name");
    }

    [Fact]
    public void AbsentFields_FailCleanlyInsteadOfThrowing()
    {
        // Arrange: both JSON keys missing deserializes to nulls
        var command = new CreateCategoryCommand(new CreateCategoryRequest(null!, null!));

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.Equal(["name", "image"], result.Errors.Select(e => e.PropertyName).Order().Reverse());
    }

    [Fact]
    public void OverlongImageUrl_Fails()
    {
        // Arrange: an unbounded URL would be forwarded to the upstream as-is
        var command = Command(image: $"https://img.example/{new string('x', 2100)}.png");

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.Contains(result.Errors, e => e.PropertyName == "image");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("/relative/path.png")]
    [InlineData("ftp://files.example/c.png")] // absolute, but a consumer rendering the category cannot use it
    public void ImageThatIsNotAnAbsoluteHttpUrl_Fails(string image)
    {
        // Arrange, Act
        var result = _validator.Validate(Command(image: image));

        // Assert
        Assert.Contains(result.Errors, e => e.PropertyName == "image");
    }
}

public class GetCategoryByIdQueryValidatorTests
{
    private readonly GetCategoryByIdQueryValidator _validator = new();

    [Fact]
    public void PositiveId_Passes()
    {
        // Arrange, Act
        var result = _validator.Validate(new GetCategoryByIdQuery(1));

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveId_Fails(int id)
    {
        // Arrange: rejected here so a nonsensical id never spends an upstream call
        var query = new GetCategoryByIdQuery(id);

        // Act
        var result = _validator.Validate(query);

        // Assert
        Assert.Contains(result.Errors, e => e.PropertyName == "id");
    }
}
