using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;

namespace SnmpCollector.HealthChecks;

/// <summary>
/// Writes health check results as JSON including correlationId, pod name, and role.
/// Wired as ResponseWriter on all three probe endpoints in Program.cs.
/// </summary>
public static class HealthCheckJsonWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static Task WriteResponse(HttpContext context, HealthReport report)
    {
        var correlation = context.RequestServices.GetService<ICorrelationService>();
        var leader = context.RequestServices.GetService<ILeaderElection>();

        var response = new
        {
            status = report.Status.ToString(),
            correlationId = correlation?.CurrentCorrelationId,
            pod = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName,
            role = leader?.CurrentRole,
            totalDuration = $"{report.TotalDuration.TotalMilliseconds:F1}ms",
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = $"{e.Value.Duration.TotalMilliseconds:F1}ms",
                data = e.Value.Data.Count > 0 ? e.Value.Data : null
            })
        };

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(
            JsonSerializer.Serialize(response, JsonOptions));
    }
}
