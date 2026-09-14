using System.Globalization;
using System.Text.Json;
using CSharpApp.Infrastructure.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CSharpApp.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness answers if the process runs; readiness also asks the upstream. Degraded still answers 200:
        // taking every instance out of rotation for a third party's outage would turn a partial failure into a
        // total one, and the error handler already turns each affected call into an honest 502/503.
        app.MapHealthChecks("/health/live", Options(_ => false)).WithMetadata(new QuietRequestTimingMetadata());
        app.MapHealthChecks("/health/ready", Options(registration => registration.Tags.Contains(PlatziApiHealthCheck.ReadyTag)))
            .WithMetadata(new QuietRequestTimingMetadata());
        return app;
    }

    private static HealthCheckOptions Options(Func<HealthCheckRegistration, bool> predicate) => new()
    {
        Predicate = predicate,
        ResultStatusCodes =
        {
            [HealthStatus.Healthy] = StatusCodes.Status200OK,
            [HealthStatus.Degraded] = StatusCodes.Status200OK,
            [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
        },
        ResponseWriter = WriteReportAsync,
    };

    /// <summary>Status, checks and their public data only. The endpoint is unauthenticated, so no exception details.</summary>
    public static async Task WriteReportAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        await using var writer = new Utf8JsonWriter(context.Response.Body);

        writer.WriteStartObject();
        writer.WriteString("status", report.Status.ToString());
        writer.WriteNumber("totalDurationMs", Math.Round(report.TotalDuration.TotalMilliseconds, 1));
        writer.WriteStartArray("checks");
        foreach (var (name, entry) in report.Entries)
        {
            writer.WriteStartObject();
            writer.WriteString("name", name);
            writer.WriteString("status", entry.Status.ToString());
            writer.WriteString("description", entry.Description);
            writer.WriteNumber("durationMs", Math.Round(entry.Duration.TotalMilliseconds, 1));
            if (entry.Data.Count > 0)
            {
                writer.WriteStartObject("data");
                foreach (var (key, value) in entry.Data)
                {
                    WriteValue(writer, key, value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(context.RequestAborted);
    }

    private static void WriteValue(Utf8JsonWriter writer, string key, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNull(key); break;
            case int number: writer.WriteNumber(key, number); break;
            case long number: writer.WriteNumber(key, number); break;
            case double number: writer.WriteNumber(key, number); break;
            case float number: writer.WriteNumber(key, number); break;
            case decimal number: writer.WriteNumber(key, number); break;
            case bool flag: writer.WriteBoolean(key, flag); break;
            default: writer.WriteString(key, Convert.ToString(value, CultureInfo.InvariantCulture)); break;
        }
    }
}
