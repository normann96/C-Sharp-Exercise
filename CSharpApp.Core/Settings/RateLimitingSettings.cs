namespace CSharpApp.Core.Settings;

public sealed class RateLimitingSettings
{
    /// <summary>Requests one caller may make per window. A per-caller allowance, not the service's total capacity.</summary>
    [Range(1, 10_000)]
    public int PermitLimit { get; set; } = 100;

    /// <summary>Length of the fixed window, in seconds. Capped at an hour: a longer fixed window would let a caller spend an entire day's allowance in one burst and then wait.</summary>
    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;
}
