namespace CSharpApp.Core.Settings;

public sealed class RestApiSettings
{
    private const string RelativePathPattern = @"^/?[A-Za-z0-9][A-Za-z0-9/_.-]*$";
    private const string RelativePathMessage = "The {0} field must be a relative resource path such as 'products' or '/auth/login'.";

    /// <summary>Absolute http(s) URL of the upstream API root; the resource paths below are resolved relative to it.</summary>
    [Required, AbsoluteHttpUrl(RequireHttps = true)]
    public string? BaseUrl { get; set; }

    [Required, RegularExpression(RelativePathPattern, ErrorMessage = RelativePathMessage)]
    public string? Products { get; set; }

    [Required, RegularExpression(RelativePathPattern, ErrorMessage = RelativePathMessage)]
    public string? Categories { get; set; }

    [Required, RegularExpression(RelativePathPattern, ErrorMessage = RelativePathMessage)]
    public string? Auth { get; set; }

    [Required]
    public string? Username { get; set; }

    [Required]
    public string? Password { get; set; }
}
