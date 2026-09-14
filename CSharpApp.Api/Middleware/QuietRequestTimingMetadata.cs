namespace CSharpApp.Api.Middleware;

/// <summary>
/// Marks an endpoint whose requests are timed but logged at Debug: probes that hit every few seconds would
/// otherwise flood the log, and a readiness probe paying an upstream round-trip would trip the slow-request warning.
/// </summary>
public sealed class QuietRequestTimingMetadata;
