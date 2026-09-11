using CSharpApp.Core.Extensions;

namespace CSharpApp.Application.Categories;

public sealed record CreateCategoryCommand(CreateCategoryRequest Category) : IRequest<Category>;

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    private const int MaxNameLength = 100;
    private const int MaxUrlLength = 2048;

    public CreateCategoryCommandValidator()
    {
        // Property names are overridden to the JSON names the caller sent, so the 400 response keys match the
        // request body instead of exposing our command shape.
        RuleFor(command => command.Category.Name).NotEmpty().MaximumLength(MaxNameLength).OverridePropertyName("name");
        RuleFor(command => command.Category.Image)
            .Must(image => image.IsAbsoluteHttpUrl())
            .WithMessage("'{PropertyName}' must be an absolute http or https URL.")
            .MaximumLength(MaxUrlLength)
            .OverridePropertyName("image");
    }
}

public sealed class CreateCategoryCommandHandler(IPlatziStoreClient client) : IRequestHandler<CreateCategoryCommand, Category>
{
    public Task<Category> Handle(CreateCategoryCommand request, CancellationToken ct) => client.CreateCategoryAsync(request.Category, ct);
}
