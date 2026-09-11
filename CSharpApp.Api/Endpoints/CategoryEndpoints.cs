using Asp.Versioning.Builder;
using CSharpApp.Application.Categories;
using CSharpApp.Core.Dtos;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CSharpApp.Api.Endpoints;

public static class CategoryEndpoints
{
    public static IVersionedEndpointRouteBuilder MapCategoryEndpoints(this IVersionedEndpointRouteBuilder api)
    {
        var categories = api.MapGroup("api/v{version:apiVersion}/categories").WithTags("Categories").HasApiVersion(1.0)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        categories.MapGet("/", async Task<Ok<IReadOnlyList<Category>>> (ISender sender, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new GetCategoriesQuery(), ct)))
            .WithName("GetCategories");

        categories.MapGet("/{id:int}", async Task<Results<Ok<Category>, NotFound>> (int id, ISender sender, CancellationToken ct) =>
                await sender.Send(new GetCategoryByIdQuery(id), ct) is { } category
                    ? TypedResults.Ok(category)
                    : TypedResults.NotFound())
            .WithName("GetCategoryById")
            .ProducesValidationProblem();

        categories.MapPost("/", async Task<CreatedAtRoute<Category>> (CreateCategoryRequest request, ISender sender, CancellationToken ct) =>
            {
                var created = await sender.Send(new CreateCategoryCommand(request), ct);
                // The path comes from the named route; only the version segment is supplied here.
                return TypedResults.CreatedAtRoute(created, "GetCategoryById", new { version = "1", id = created.Id });
            })
            .WithName("CreateCategory")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return api;
    }
}
