namespace CSharpApp.Core.Interfaces;

public interface IPlatziStoreClient
{
    /// <remarks>Paging is forwarded only when both <paramref name="limit"/> and <paramref name="offset"/> are given.</remarks>
    Task<IReadOnlyList<Product>> GetProductsAsync(int? limit, int? offset, CancellationToken ct);

    /// <returns>The product, or <c>null</c> when the upstream does not know the id.</returns>
    Task<Product?> GetProductAsync(int id, CancellationToken ct);

    Task<Product> CreateProductAsync(CreateProductRequest request, CancellationToken ct);

    /// <remarks>No paging: the upstream honours a limit but silently ignores an offset, so a page after the first is unreachable.</remarks>
    Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken ct);

    /// <returns>The category, or <c>null</c> when the upstream does not know the id.</returns>
    Task<Category?> GetCategoryAsync(int id, CancellationToken ct);

    Task<Category> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct);
}
