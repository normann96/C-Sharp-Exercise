using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CSharpApp.Api.OpenApi;

/// <summary>
/// Route templates leave "v{version}" in the generated paths, but no caller can fill that placeholder: the
/// version is bound from the route, not supplied as a parameter. A path template without a matching parameter
/// makes the document invalid, so the document's own version is substituted here.
/// </summary>
internal sealed class VersionedPathDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Paths is null)
        {
            return Task.CompletedTask;
        }

        var rewritten = new OpenApiPaths();
        foreach (var (path, item) in document.Paths)
        {
            rewritten.Add(path.Replace("v{version}", context.DocumentName, StringComparison.Ordinal), item);
        }

        document.Paths = rewritten;
        return Task.CompletedTask;
    }
}
