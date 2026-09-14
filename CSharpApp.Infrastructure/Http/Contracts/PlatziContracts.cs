namespace CSharpApp.Infrastructure.Http.Contracts;

/// <summary>The upstream's shapes as they arrive. Everything is optional, which is the truth about a schema we do
/// not control; keeping them separate is what lets the published shapes be something better.</summary>
public sealed class PlatziProduct
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
    [JsonPropertyName("price")] public decimal? Price { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("images")] public List<string> Images { get; set; } = [];
    [JsonPropertyName("creationAt")] public DateTime? CreationAt { get; set; }
    [JsonPropertyName("updatedAt")] public DateTime? UpdatedAt { get; set; }
    [JsonPropertyName("category")] public PlatziCategory? Category { get; set; }
}

public sealed class PlatziCategory
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
    [JsonPropertyName("image")] public string? Image { get; set; }
    [JsonPropertyName("creationAt")] public DateTime? CreationAt { get; set; }
    [JsonPropertyName("updatedAt")] public DateTime? UpdatedAt { get; set; }
}
