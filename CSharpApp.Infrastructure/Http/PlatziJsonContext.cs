namespace CSharpApp.Infrastructure.Http;

[JsonSerializable(typeof(Product))]
[JsonSerializable(typeof(List<Product>))]
[JsonSerializable(typeof(Category))]
[JsonSerializable(typeof(List<Category>))]
[JsonSerializable(typeof(CreateProductRequest))]
[JsonSerializable(typeof(CreateCategoryRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(AuthTokenResponse))]
public sealed partial class PlatziJsonContext : JsonSerializerContext;
