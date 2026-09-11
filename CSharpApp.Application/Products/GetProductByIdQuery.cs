namespace CSharpApp.Application.Products;

public sealed record GetProductByIdQuery(int Id) : IRequest<Product?>;

public sealed class GetProductByIdQueryValidator : AbstractValidator<GetProductByIdQuery>
{
    public GetProductByIdQueryValidator() => RuleFor(query => query.Id).GreaterThan(0).OverridePropertyName("id");
}

public sealed class GetProductByIdQueryHandler(IPlatziStoreClient client) : IRequestHandler<GetProductByIdQuery, Product?>
{
    public Task<Product?> Handle(GetProductByIdQuery request, CancellationToken ct) => client.GetProductAsync(request.Id, ct);
}
