namespace CSharpApp.Application.Products;

public class ProductsService(IPlatziStoreClient client) : IProductsService
{
    public async Task<IReadOnlyCollection<Product>> GetProducts()
        => await client.GetProductsAsync(limit: null, offset: null, CancellationToken.None);
}
