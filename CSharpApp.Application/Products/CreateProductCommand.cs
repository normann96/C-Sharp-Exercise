using CSharpApp.Core.Extensions;

namespace CSharpApp.Application.Products;

public sealed record CreateProductCommand(CreateProductRequest Product) : IRequest<Product>;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        // Property names are overridden to the JSON names the caller sent, so the 400 response keys match the
        // request body instead of exposing our command shape.
        RuleFor(command => command.Product.Title).NotEmpty().MaximumLength(200).OverridePropertyName("title");
        RuleFor(command => command.Product.Price).GreaterThan(0).OverridePropertyName("price");
        RuleFor(command => command.Product.Description).NotEmpty().MaximumLength(2000).OverridePropertyName("description");
        RuleFor(command => command.Product.CategoryId).GreaterThan(0).WithName("categoryId").OverridePropertyName("categoryId");
        RuleFor(command => command.Product.Images).NotEmpty().Must(images => images.Count <= 10)
            .WithMessage("A product may carry at most 10 images.").OverridePropertyName("images");
        RuleForEach(command => command.Product.Images)
            .Must(image => image.IsAbsoluteHttpUrl())
            .WithMessage("Each image must be an absolute http or https URL.")
            .OverridePropertyName("images");
    }
}

public sealed class CreateProductCommandHandler(IPlatziStoreClient client) : IRequestHandler<CreateProductCommand, Product>
{
    public Task<Product> Handle(CreateProductCommand request, CancellationToken ct) => client.CreateProductAsync(request.Product, ct);
}
