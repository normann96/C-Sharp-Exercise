namespace CSharpApp.Application.Products;

public sealed record GetProductsQuery(int? Limit, int? Offset) : IRequest<IReadOnlyList<Product>>;

public sealed class GetProductsQueryValidator : AbstractValidator<GetProductsQuery>
{
    public GetProductsQueryValidator()
    {
        RuleFor(query => query.Limit).InclusiveBetween(1, 100).OverridePropertyName("limit");
        RuleFor(query => query.Offset).GreaterThanOrEqualTo(0).OverridePropertyName("offset");

        // The upstream ignores one without the other and silently returns the whole list.
        RuleFor(query => query)
            .Must(query => (query.Limit is null) == (query.Offset is null))
            .WithMessage("limit and offset must be provided together.")
            .OverridePropertyName("paging");
    }
}

public sealed class GetProductsQueryHandler(IPlatziStoreClient client) : IRequestHandler<GetProductsQuery, IReadOnlyList<Product>>
{
    public Task<IReadOnlyList<Product>> Handle(GetProductsQuery request, CancellationToken ct)
        => client.GetProductsAsync(request.Limit, request.Offset, ct);
}
