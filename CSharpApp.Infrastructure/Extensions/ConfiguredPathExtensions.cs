namespace CSharpApp.Infrastructure.Extensions;

internal static class ConfiguredPathExtensions
{
    /// <summary>
    /// Normalizes a configured resource path. The provided configuration mixes "products" and "/categories";
    /// resolved against a BaseAddress, a leading slash escapes its /api/v1/ path.
    /// </summary>
    public static string AsRelativePath(this string? configuredPath) => (configuredPath ?? string.Empty).Trim().Trim('/');
}
