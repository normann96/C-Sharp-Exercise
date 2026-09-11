namespace CSharpApp.Application.Categories;

public sealed record GetCategoriesQuery : IRequest<IReadOnlyList<Category>>;

public sealed class GetCategoriesQueryHandler(IPlatziStoreClient client) : IRequestHandler<GetCategoriesQuery, IReadOnlyList<Category>>
{
    public Task<IReadOnlyList<Category>> Handle(GetCategoriesQuery request, CancellationToken ct) => client.GetCategoriesAsync(ct);
}
