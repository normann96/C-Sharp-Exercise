namespace CSharpApp.Core.Settings;

public sealed class RestApiSettings
{
    /// <summary>Absolute http(s) URL of the upstream API root; the resource paths below are resolved relative to it.</summary>
    [Required, AbsoluteHttpUrl]
    public string? BaseUrl { get; set; }

    [Required]
    public string? Products { get; set; }

    [Required]
    public string? Categories { get; set; }

    [Required]
    public string? Auth { get; set; }

    [Required]
    public string? Username { get; set; }

    [Required]
    public string? Password { get; set; }
}
