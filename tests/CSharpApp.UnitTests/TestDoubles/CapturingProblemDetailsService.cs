using Microsoft.AspNetCore.Http;

namespace CSharpApp.UnitTests.TestDoubles;

/// <summary>Records the ProblemDetails a handler asked to write instead of serializing it.</summary>
public sealed class CapturingProblemDetailsService : IProblemDetailsService
{
    public ProblemDetailsContext? Written { get; private set; }

    /// <summary>The real service refuses when no writer accepts the request's Accept header.</summary>
    public bool CanWrite { get; set; } = true;

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        Written = context;
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
    {
        if (CanWrite)
        {
            Written = context;
        }

        return ValueTask.FromResult(CanWrite);
    }
}
