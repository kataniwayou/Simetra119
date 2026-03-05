using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SnmpCollector.Extensions;
using SnmpCollector.Pipeline;

var builder = WebApplication.CreateBuilder(args);

// DI registration order:
// 1. Telemetry    (registered first = disposed last = ForceFlush on shutdown)
// 2. Configuration
// 3. Pipeline     (MediatR + behaviors)
// 4. Scheduling   (Quartz + jobs + liveness registry)
// 5. HealthChecks (startup, readiness, liveness probes)
// 6. Lifecycle    (GracefulShutdownService -- MUST BE LAST, stops FIRST)
builder.AddSnmpTelemetry();
builder.Services.AddSnmpConfiguration(builder.Configuration);
builder.Services.AddSnmpPipeline();
builder.Services.AddSnmpScheduling(builder.Configuration);
builder.Services.AddSnmpHealthChecks();     // Phase 8: health probe checks
builder.Services.AddSnmpLifecycle();        // Phase 8: MUST BE LAST (SHUT-01)

var app = builder.Build();

// Seed first correlation ID before any Quartz job fires (before Run starts hosted services)
var correlationService = app.Services.GetRequiredService<ICorrelationService>();
correlationService.SetCorrelationId(Guid.NewGuid().ToString("N"));

// Phase 8: Health probe endpoints with tag-filtered checks and explicit status codes.
// Each endpoint runs only the health check(s) matching its tag.
app.MapHealthChecks("/healthz/startup", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("startup"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

try
{
    app.Run();
}
catch (OptionsValidationException ex)
{
    // Fail-fast: surface all validation failures clearly before the host accepts work
    Console.Error.WriteLine("Configuration validation failed:");
    foreach (var failure in ex.Failures)
    {
        Console.Error.WriteLine($"  - {failure}");
    }

    throw;
}
