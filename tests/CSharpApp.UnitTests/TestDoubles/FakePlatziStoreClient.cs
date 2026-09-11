using CSharpApp.Core.Dtos;
using CSharpApp.Core.Interfaces;

namespace CSharpApp.UnitTests.TestDoubles;

/// <summary>Records the arguments each handler forwarded and answers with whatever the test set up.</summary>
public sealed class FakePlatziStoreClient : IPlatziStoreClient
{
    public (int? Limit, int? Offset)? ProductsQuery { get; private set; }
    public int? RequestedProductId { get; private set; }
    public CreateProductRequest? CreatedProduct { get; private set; }
    public int? RequestedCategoryId { get; private set; }
    public CreateCategoryRequest? CreatedCategory { get; private set; }

    public IReadOnlyList<Product> Products { get; init; } = [];
    public Product? Product { get; init; }
    public IReadOnlyList<Category> Categories { get; init; } = [];
    public Category? Category { get; init; }

    public Task<IReadOnlyList<Product>> GetProductsAsync(int? limit, int? offset, CancellationToken ct)
    {
        ProductsQuery = (limit, offset);
        return Task.FromResult(Products);
    }

    public Task<Product?> GetProductAsync(int id, CancellationToken ct)
    {
        RequestedProductId = id;
        return Task.FromResult(Product);
    }

    public Task<Product> CreateProductAsync(CreateProductRequest request, CancellationToken ct)
    {
        CreatedProduct = request;
        return Task.FromResult(Product ?? throw new InvalidOperationException($"The test did not set {nameof(Product)}."));
    }

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken ct) => Task.FromResult(Categories);

    public Task<Category?> GetCategoryAsync(int id, CancellationToken ct)
    {
        RequestedCategoryId = id;
        return Task.FromResult(Category);
    }

    public Task<Category> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        CreatedCategory = request;
        return Task.FromResult(Category ?? throw new InvalidOperationException($"The test did not set {nameof(Category)}."));
    }
}
