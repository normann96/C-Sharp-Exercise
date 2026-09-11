using CSharpApp.Application.Common;
using CSharpApp.Application.Products;
using CSharpApp.Core.Dtos;
using FluentValidation;
using MediatR;

namespace CSharpApp.UnitTests.Application;

public class ValidationBehaviorTests
{
    private static readonly GetProductByIdQuery ValidQuery = new(1);
    private static readonly GetProductByIdQuery InvalidQuery = new(0);

    private static Task<Product> Handled() => Task.FromResult(new Product { Id = 1 });

    private static ValidationBehavior<GetProductByIdQuery, Product> Behavior(params IValidator<GetProductByIdQuery>[] validators)
        => new(validators);

    [Fact]
    public async Task ValidRequest_ReachesTheHandler()
    {
        // Arrange
        var handlerCalled = false;
        var behavior = Behavior(new GetProductByIdQueryValidator());

        // Act
        await behavior.Handle(ValidQuery, _ => { handlerCalled = true; return Handled(); }, CancellationToken.None);

        // Assert
        Assert.True(handlerCalled);
    }

    [Fact]
    public async Task InvalidRequest_NeverReachesTheHandler()
    {
        // Arrange: validation runs before the handler, so a bad request costs no upstream call
        var handlerCalled = false;
        var behavior = Behavior(new GetProductByIdQueryValidator());

        // Act
        var act = () => behavior.Handle(InvalidQuery, _ => { handlerCalled = true; return Handled(); }, CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ValidationException>(act);
        Assert.False(handlerCalled);
    }

    [Fact]
    public async Task SeveralValidators_ReportEachFailureOnce()
    {
        // Arrange: validators share nothing, so two of them must not report each other's failures
        var behavior = Behavior(new GetProductByIdQueryValidator(), new AlwaysFailsValidator());

        // Act
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => behavior.Handle(InvalidQuery, _ => Handled(), CancellationToken.None));

        // Assert
        Assert.Equal(2, exception.Errors.Count());
    }

    [Fact]
    public async Task RequestWithoutValidators_ReachesTheHandler()
    {
        // Arrange
        var handlerCalled = false;
        var behavior = Behavior();

        // Act
        await behavior.Handle(InvalidQuery, _ => { handlerCalled = true; return Handled(); }, CancellationToken.None);

        // Assert
        Assert.True(handlerCalled);
    }
}

file sealed class AlwaysFailsValidator : AbstractValidator<GetProductByIdQuery>
{
    public AlwaysFailsValidator() => RuleFor(query => query.Id).Must(_ => false).WithMessage("always fails");
}
