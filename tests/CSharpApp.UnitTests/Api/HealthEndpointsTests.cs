using System.Text.Json;
using CSharpApp.Api.Endpoints;
using CSharpApp.Infrastructure.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CSharpApp.UnitTests.Api;

public class HealthEndpointsTests
{
    private const string Secret = "internal-detail-that-must-not-leak";

    private readonly DefaultHttpContext _context = new() { Response = { Body = new MemoryStream() } };

    private static HealthReport Report(HealthStatus status, params (string Name, HealthReportEntry Entry)[] entries)
        => new(entries.ToDictionary(e => e.Name, e => e.Entry), status, TimeSpan.FromMilliseconds(12.5));

    private async Task<JsonDocument> WrittenAsync()
    {
        _context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(_context.Response.Body);
    }

    [Fact]
    public async Task WritesTheOverallStatusAndEachCheck()
    {
        // Arrange
        var report = Report(HealthStatus.Degraded,
            (PlatziApiHealthCheck.Name, new HealthReportEntry(HealthStatus.Degraded, "Upstream API returned 503", TimeSpan.FromMilliseconds(11),
                exception: null, data: new Dictionary<string, object> { ["statusCode"] = 503 })));

        // Act
        await HealthEndpoints.WriteReportAsync(_context, report);
        using var json = await WrittenAsync();

        // Assert
        Assert.StartsWith("application/json", _context.Response.ContentType);
        Assert.Equal("Degraded", json.RootElement.GetProperty("status").GetString());
        var check = Assert.Single(json.RootElement.GetProperty("checks").EnumerateArray());
        Assert.Equal(PlatziApiHealthCheck.Name, check.GetProperty("name").GetString());
        Assert.Equal("Degraded", check.GetProperty("status").GetString());
        Assert.Equal("Upstream API returned 503", check.GetProperty("description").GetString());
        Assert.Equal(503, check.GetProperty("data").GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task NeverWritesExceptionDetails()
    {
        // Arrange: a health endpoint is unauthenticated; an exception message is internal diagnostics
        var report = Report(HealthStatus.Degraded,
            (PlatziApiHealthCheck.Name, new HealthReportEntry(HealthStatus.Degraded, "Upstream API unreachable", TimeSpan.FromMilliseconds(3),
                new HttpRequestException(Secret), data: null)));

        // Act
        await HealthEndpoints.WriteReportAsync(_context, report);
        _context.Response.Body.Position = 0;
        var body = await new StreamReader(_context.Response.Body).ReadToEndAsync();

        // Assert
        Assert.DoesNotContain(Secret, body);
    }

    [Fact]
    public async Task DataValues_AreWrittenAsScalars_CultureInvariant()
    {
        // Arrange: a Greek host must not write 1,5 into a JSON document, and a null must not become a 500
        var report = Report(HealthStatus.Healthy,
            (PlatziApiHealthCheck.Name, new HealthReportEntry(HealthStatus.Healthy, "ok", TimeSpan.Zero, exception: null,
                data: new Dictionary<string, object> { ["ratio"] = 1.5m, ["when"] = TimeSpan.FromSeconds(1.5), ["missing"] = null! })));

        // Act
        await HealthEndpoints.WriteReportAsync(_context, report);
        using var json = await WrittenAsync();

        // Assert
        var data = Assert.Single(json.RootElement.GetProperty("checks").EnumerateArray()).GetProperty("data");
        Assert.Equal(1.5m, data.GetProperty("ratio").GetDecimal());
        Assert.Equal("00:00:01.5000000", data.GetProperty("when").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("missing").ValueKind);
    }

    [Fact]
    public async Task LivenessWithNoChecks_IsHealthyWithAnEmptyList()
    {
        // Arrange
        var report = Report(HealthStatus.Healthy);

        // Act
        await HealthEndpoints.WriteReportAsync(_context, report);
        using var json = await WrittenAsync();

        // Assert
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        Assert.Empty(json.RootElement.GetProperty("checks").EnumerateArray());
    }
}
