namespace CSharpApp.Core.Dtos;

public sealed record CreateProductRequest(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("categoryId")] int CategoryId,
    [property: JsonPropertyName("images")] IReadOnlyList<string> Images);
