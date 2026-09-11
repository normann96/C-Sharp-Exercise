using CSharpApp.Core.Extensions;

namespace CSharpApp.Application.Products;

public sealed record CreateProductCommand(CreateProductRequest Product) : IRequest<Product>;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    private const int MaxImages = 10;
    private const int MaxUrlLength = 2048;

    public CreateProductCommandValidator()
    {
        // Property names are overridden to the JSON names the caller sent, so the 400 response keys match the
        // request body instead of exposing our command shape.
        RuleFor(command => command.Product.Title).NotEmpty().MaximumLength(200).OverridePropertyName("title");
        RuleFor(command => command.Product.Price).GreaterThan(0).OverridePropertyName("price");
        RuleFor(command => command.Product.Description).NotEmpty().MaximumLength(2000).OverridePropertyName("description");
        RuleFor(command => command.Product.CategoryId).GreaterThan(0).WithName("categoryId").OverridePropertyName("categoryId");
        // The rules after NotEmpty still run when it fails, so they must tolerate an absent "images" key.
        RuleFor(command => command.Product.Images)
            .NotEmpty()
            .Must(images => images is null || images.Count <= MaxImages)
            .WithMessage($"A product may carry at most {MaxImages} images.")
            .OverridePropertyName("images");
        RuleForEach(command => command.Product.Images)
            .Must(image => image.IsAbsoluteHttpUrl())
            .WithMessage("Each image must be an absolute http or https URL.")
            .MaximumLength(MaxUrlLength)
            .OverridePropertyName("images");
    }
}

public sealed class CreateProductCommandHandler(IPlatziStoreClient client) : IRequestHandler<CreateProductCommand, Product>
{
    public Task<Product> Handle(CreateProductCommand request, CancellationToken ct) => client.CreateProductAsync(request.Product, ct);
}
