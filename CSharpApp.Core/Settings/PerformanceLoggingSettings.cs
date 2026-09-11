namespace CSharpApp.Core.Settings;

public sealed class PerformanceLoggingSettings
{
    /// <summary>Requests slower than this, in milliseconds, are logged as warnings instead of information. Capped at the 60 s one upstream call can take at most, since a higher threshold could never fire.</summary>
    [Range(1, 60_000)]
    public int SlowRequestThresholdMs { get; set; } = 1000;
}
