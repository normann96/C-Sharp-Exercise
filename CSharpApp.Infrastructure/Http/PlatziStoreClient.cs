namespace CSharpApp.Infrastructure.Http;

public sealed class PlatziStoreClient(HttpClient http, IOptions<RestApiSettings> options) : IPlatziStoreClient
{
    // Only the two resource paths are kept; the client has no business holding the credentials.
    private readonly string _productsPath = options.Value.Products.AsRelativePath();
    private readonly string _categoriesPath = options.Value.Categories.AsRelativePath();

    public async Task<IReadOnlyList<Product>> GetProductsAsync(int? limit, int? offset, CancellationToken ct)
    {
        var url = limit is not null && offset is not null ? $"{_productsPath}?limit={limit}&offset={offset}" : _productsPath;

        // Buffered rather than streamed, so the response size cap and the attempt timeout cover the body too.
        using var response = await http.GetAsync(url, ct);
        await response.EnsureUpstreamSuccessAsync(ct);
        var upstream = await response.Content.ReadFromJsonAsync(UpstreamJsonContext.Default.ListPlatziProduct, ct);
        return (upstream ?? []).Adapt<List<Product>>();
    }

    public async Task<Product?> GetProductAsync(int id, CancellationToken ct)
    {
        using var response = await http.GetAsync($"{_productsPath}/{id}", ct);
        if (await response.IsUpstreamNotFoundAsync(ct))
        {
            return null;
        }

        await response.EnsureUpstreamSuccessAsync(ct);
        return (await response.Content.ReadFromJsonAsync(UpstreamJsonContext.Default.PlatziProduct, ct))?.Adapt<Product>();
    }

    public async Task<Product> CreateProductAsync(CreateProductRequest request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(_productsPath, request, PlatziJsonContext.Default.CreateProductRequest, ct);
        await response.ThrowIfRejectedAsync(ct);
        await response.EnsureUpstreamSuccessAsync(ct);
        var created = (await response.Content.ReadFromJsonAsync(UpstreamJsonContext.Default.PlatziProduct, ct))?.Adapt<Product>()
                      ?? throw new HttpRequestException("Upstream API returned an empty body for the created product.");
        return created.Id is not null
            ? created
            : throw new HttpRequestException("Upstream API returned a created product without an id.");
    }

    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync(_categoriesPath, ct);
        await response.EnsureUpstreamSuccessAsync(ct);
        var upstream = await response.Content.ReadFromJsonAsync(UpstreamJsonContext.Default.ListPlatziCategory, ct);
        return (upstream ?? []).Adapt<List<Category>>();
    }

    public async Task<Category?> GetCategoryAsync(int id, CancellationToken ct)
    {
        using var response = await http.GetAsync($"{_categoriesPath}/{id}", ct);
        if (await response.IsUpstreamNotFoundAsync(ct))
        {
            return null;
        }

        await response.EnsureUpstreamSuccessAsync(ct);
        return (await response.Content.ReadFromJsonAsync(UpstreamJsonContext.Default.PlatziCategory, ct))?.Adapt<Category>();
    }

    public async Task<Category> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(_categoriesPath, request, PlatziJsonContext.Default.CreateCategoryRequest, ct);
        await response.ThrowIfRejectedAsync(ct);
        await response.EnsureUpstreamSuccessAsync(ct);
        var created = (await response.Content.ReadFromJsonAsync(UpstreamJsonContext.Default.PlatziCategory, ct))?.Adapt<Category>()
                      ?? throw new HttpRequestException("Upstream API returned an empty body for the created category.");
        return created.Id is not null
            ? created
            : throw new HttpRequestException("Upstream API returned a created category without an id.");
    }
}
