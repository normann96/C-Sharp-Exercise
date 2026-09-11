namespace CSharpApp.Core.Settings;

/// <summary>
/// Accepts only absolute http/https URLs. The BCL <see cref="UrlAttribute"/> is a scheme-prefix check
/// ("https://" alone passes), which is not enough for a value that becomes an <see cref="HttpClient.BaseAddress"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class AbsoluteHttpUrlAttribute() : ValidationAttribute("The {0} field must be an absolute http or https URL.")
{
    public override bool IsValid(object? value) => value switch
    {
        null => true,                                   // presence is [Required]'s job
        string s when string.IsNullOrWhiteSpace(s) => true,
        string s => Uri.TryCreate(s, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
        _ => false
    };
}
