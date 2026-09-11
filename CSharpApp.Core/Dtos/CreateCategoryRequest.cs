namespace CSharpApp.Core.Dtos;

public sealed record CreateCategoryRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("image")] string Image);
