using CSharpApp.Core.Extensions;

namespace CSharpApp.Core.Settings;

/// <summary>
/// Accepts only absolute http/https URLs. The BCL <see cref="UrlAttribute"/> is a scheme-prefix check
/// ("https://" alone passes), which is not enough for a value that becomes an <see cref="HttpClient.BaseAddress"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class AbsoluteHttpUrlAttribute() : ValidationAttribute("The {0} field must be an absolute http or https URL.")
{
    /// <summary>Refuse plain http unless the host is loopback, for a value that will carry credentials.</summary>
    public bool RequireHttps { get; set; }

    public override bool IsValid(object? value) => value switch
    {
        null => true,                                   // presence is [Required]'s job
        string s when string.IsNullOrWhiteSpace(s) => true,
        string s => s.IsAbsoluteHttpUrl() && (!RequireHttps || IsEncryptedOrLocal(s)),
        _ => false
    };

    public override string FormatErrorMessage(string name)
        => RequireHttps
            ? $"The {name} field must be an absolute https URL; plain http is accepted only for a loopback host."
            : base.FormatErrorMessage(name);

    // Loopback stays allowed so a mock upstream on this machine still works.
    private static bool IsEncryptedOrLocal(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback);
}
