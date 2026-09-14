using Asp.Versioning.Builder;
using CSharpApp.Application.Products;
using CSharpApp.Core.Dtos;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;

namespace CSharpApp.Api.Endpoints;

public static class ProductEndpoints
{
    public static IVersionedEndpointRouteBuilder MapProductEndpoints(this IVersionedEndpointRouteBuilder api)
    {
        var products = api.MapGroup("api/v{version:apiVersion}/products")
            .WithTags("Products")
            .HasApiVersion(1.0)
            .RequireRateLimiting(RateLimitingExtensions.PolicyName)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        products.MapGet("/", async Task<Ok<IReadOnlyList<Product>>> (ISender sender, int? limit, int? offset, CancellationToken ct) =>
                TypedResults.Ok(await sender.Send(new GetProductsQuery(limit, offset), ct)))
            .WithName("GetProducts")
            .ProducesValidationProblem();

        products.MapGet("/{id:int}", async Task<Results<Ok<Product>, NotFound>> (int id, ISender sender, CancellationToken ct) =>
                await sender.Send(new GetProductByIdQuery(id), ct) is { } product
                    ? TypedResults.Ok(product)
                    : TypedResults.NotFound())
            .WithName("GetProductById")
            .ProducesValidationProblem();

        products.MapPost("/", async Task<CreatedAtRoute<Product>> (CreateProductRequest request, ISender sender, CancellationToken ct) =>
            {
                var created = await sender.Send(new CreateProductCommand(request), ct);
                // The path comes from the named route; only the version segment is supplied here.
                return TypedResults.CreatedAtRoute(created, "GetProductById", new { version = "1", id = created.Id });
            })
            .WithName("CreateProduct")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return api;
    }
}
