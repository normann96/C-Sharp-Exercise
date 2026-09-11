namespace CSharpApp.Application.Categories;

public sealed record GetCategoryByIdQuery(int Id) : IRequest<Category?>;

public sealed class GetCategoryByIdQueryValidator : AbstractValidator<GetCategoryByIdQuery>
{
    public GetCategoryByIdQueryValidator() => RuleFor(query => query.Id).GreaterThan(0).OverridePropertyName("id");
}

public sealed class GetCategoryByIdQueryHandler(IPlatziStoreClient client) : IRequestHandler<GetCategoryByIdQuery, Category?>
{
    public Task<Category?> Handle(GetCategoryByIdQuery request, CancellationToken ct) => client.GetCategoryAsync(request.Id, ct);
}
