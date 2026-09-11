namespace CSharpApp.Infrastructure.Http;

public sealed class PlatziStoreClient(HttpClient http, IOptions<RestApiSettings> options) : IPlatziStoreClient
{
    // Only the two resource paths are kept; the client has no business holding the credentials.
    private readonly string _productsPath = RelativePath(options.Value.Products);
    private readonly string _categoriesPath = RelativePath(options.Value.Categories);

    public async Task<IReadOnlyList<Product>> GetProductsAsync(int? limit, int? offset, CancellationToken ct)
    {
        var url = limit is not null && offset is not null ? $"{_productsPath}?limit={limit}&offset={offset}" : _productsPath;
        return await http.GetFromJsonAsync(url, PlatziJsonContext.Default.ListProduct, ct) ?? [];
    }

    public async Task<Product?> GetProductAsync(int id, CancellationToken ct)
    {
        using var response = await http.GetAsync($"{_productsPath}/{id}", ct);
        if (await response.IsUpstreamNotFoundAsync(ct))
        {
            return null;
        }

        await response.EnsureUpstreamSuccessAsync(ct);
        return await response.Content.ReadFromJsonAsync(PlatziJsonContext.Default.Product, ct);
    }

    public async Task<Product> CreateProductAsync(CreateProductRequest request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(_productsPath, request, PlatziJsonContext.Default.CreateProductRequest, ct);
        await response.EnsureUpstreamSuccessAsync(ct);
        return await response.Content.ReadFromJsonAsync(PlatziJsonContext.Default.Product, ct)
               ?? throw new HttpRequestException("Upstream API returned an empty body for the created product.");
    }

    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken ct)
        => await http.GetFromJsonAsync(_categoriesPath, PlatziJsonContext.Default.ListCategory, ct) ?? [];

    public async Task<Category?> GetCategoryAsync(int id, CancellationToken ct)
    {
        using var response = await http.GetAsync($"{_categoriesPath}/{id}", ct);
        if (await response.IsUpstreamNotFoundAsync(ct))
        {
            return null;
        }

        await response.EnsureUpstreamSuccessAsync(ct);
        return await response.Content.ReadFromJsonAsync(PlatziJsonContext.Default.Category, ct);
    }

    public async Task<Category> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(_categoriesPath, request, PlatziJsonContext.Default.CreateCategoryRequest, ct);
        await response.EnsureUpstreamSuccessAsync(ct);
        return await response.Content.ReadFromJsonAsync(PlatziJsonContext.Default.Category, ct)
               ?? throw new HttpRequestException("Upstream API returned an empty body for the created category.");
    }

    // The provided configuration mixes "products" and "/categories". Resolved against a BaseAddress,
    // a leading slash escapes its /api/v1/ path, so configured paths are normalized once here.
    private static string RelativePath(string? configuredPath) => (configuredPath ?? string.Empty).Trim().Trim('/');
}
