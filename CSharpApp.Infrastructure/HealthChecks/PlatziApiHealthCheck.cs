namespace CSharpApp.Infrastructure.HealthChecks;

/// <summary>
/// Reachability of the upstream API, probed anonymously through the dedicated probe client: no token, no retries,
/// a short budget of its own. Failure is Degraded rather than Unhealthy: this service without its upstream is
/// impaired, not dead, and an orchestrator must not restart it for someone else's outage.
/// </summary>
public sealed class PlatziApiHealthCheck(IHttpClientFactory httpClientFactory, IOptions<RestApiSettings> options) : IHealthCheck
{
    public const string Name = "upstream_api";
    public const string ReadyTag = "ready";

    private readonly string _probePath = $"{options.Value.Products.AsRelativePath()}?limit=1&offset=0";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient(Configuration.HttpConfiguration.PlatziProbe);
            using var response = await client.GetAsync(_probePath, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Upstream API reachable")
                : HealthCheckResult.Degraded($"Upstream API returned {(int)response.StatusCode}",
                    data: new Dictionary<string, object> { ["statusCode"] = (int)response.StatusCode });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // the caller gave up; that says nothing about the upstream
        }
        catch (Exception exception)
        {
            // The probe's own timeout and connection failures are the upstream's state, so they are reported, not thrown.
            return HealthCheckResult.Degraded("Upstream API unreachable", exception);
        }
    }
}
