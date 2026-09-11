namespace CSharpApp.Application.Common;

/// <summary>
/// Runs every validator registered for a request before its handler, so an invalid request never reaches the
/// upstream.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!validators.Any())
        {
            return await next(ct);
        }

        // A context per validator: AbstractValidator accumulates failures into it, so sharing one would
        // report every failure once per validator, and Task.WhenAll would mutate it concurrently.
        var results = await Task.WhenAll(
            validators.Select(validator => validator.ValidateAsync(new ValidationContext<TRequest>(request), ct)));
        var failures = results.SelectMany(result => result.Errors).ToList();

        return failures.Count == 0 ? await next(ct) : throw new ValidationException(failures);
    }
}
