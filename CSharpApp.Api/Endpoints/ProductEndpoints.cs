using Asp.Versioning.Builder;
using CSharpApp.Application.Products;
using CSharpApp.Core.Dtos;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CSharpApp.Api.Endpoints;

public static class ProductEndpoints
{
    public static IVersionedEndpointRouteBuilder MapProductEndpoints(this IVersionedEndpointRouteBuilder api)
    {
        var products = api.MapGroup("api/v{version:apiVersion}/products").WithTags("Products").HasApiVersion(1.0);

        products.MapGet("/", async Task<Ok<IReadOnlyList<Product>>> (ISender sender, int? limit, int? offset, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new GetProductsQuery(limit, offset), ct)))
            .WithName("GetProducts");

        products.MapGet("/{id:int}", async Task<Results<Ok<Product>, NotFound>> (int id, ISender sender, CancellationToken ct) =>
                await sender.Send(new GetProductByIdQuery(id), ct) is { } product
                    ? TypedResults.Ok(product)
                    : TypedResults.NotFound())
            .WithName("GetProductById");

        products.MapPost("/", async Task<CreatedAtRoute<Product>> (CreateProductRequest request, ISender sender, CancellationToken ct) =>
            {
                var created = await sender.Send(new CreateProductCommand(request), ct);
                // Built from the route rather than a literal, so a future version returns its own Location.
                return TypedResults.CreatedAtRoute(created, "GetProductById", new { version = "1", id = created.Id });
            })
            .WithName("CreateProduct");

        return api;
    }
}
