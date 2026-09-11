namespace CSharpApp.Core.Settings;

public sealed class HttpClientSettings
{
    /// <summary>Pooled handler lifetime in minutes (at most a day): handlers must be recycled so DNS changes are picked up.</summary>
    [Range(1, 1440)]
    public int LifeTime { get; set; }

    /// <summary>Retry attempts per upstream call; 0 disables retries. Capped: beyond a handful of retries we are hammering a third party, not recovering.</summary>
    [Range(0, 10)]
    public int RetryCount { get; set; }

    /// <summary>Base delay between retries, in milliseconds.</summary>
    [Range(0, 60_000)]
    public int SleepDuration { get; set; }
}
