using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SnmpCollector.Extensions;
using SnmpCollector.Pipeline;

var builder = WebApplication.CreateBuilder(args);

// Config directory for appsettings.k8s.json overlay.
// K8s: CONFIG_DIRECTORY=/app/config (directory-mounted ConfigMap).
// Local dev: falls back to {ContentRootPath}/config.
// Must happen BEFORE builder.Build() -- AddJsonFile modifies ConfigurationBuilder.
var configDir = Environment.GetEnvironmentVariable("CONFIG_DIRECTORY")
    ?? Path.Combine(builder.Environment.ContentRootPath, "config");

if (Directory.Exists(configDir))
{
    // Load K8s appsettings override if present (replaces old subPath mount).
    // reloadOnChange: false -- Phase 15 ConfigMapWatcherService handles live reload via K8s API.
    var k8sConfig = Path.Combine(configDir, "appsettings.k8s.json");
    if (File.Exists(k8sConfig))
    {
        builder.Configuration.AddJsonFile(k8sConfig, optional: true, reloadOnChange: false);
    }
}

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

// Phase 15: Local dev -- load unified config from file when not in K8s.
// In K8s mode, ConfigMapWatcherService handles config loading via API watch.
// ReconcileAsync is called here for consistency with K8s mode, where
// ConfigMapWatcherService calls it after ReloadAsync. PollSchedulerStartupService
// also schedules initial jobs, but ReconcileAsync is idempotent -- it will detect
// that the desired jobs already exist and make no changes.
if (!k8s.KubernetesClientConfiguration.IsInCluster())
{
    var simetraConfigPath = Path.Combine(configDir, "simetra-config.json");
    if (File.Exists(simetraConfigPath))
    {
        var json = File.ReadAllText(simetraConfigPath);
        var jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true
        };
        var config = System.Text.Json.JsonSerializer.Deserialize<SnmpCollector.Configuration.SimetraConfigModel>(json, jsonOptions);
        if (config != null)
        {
            var oidMapService = app.Services.GetRequiredService<SnmpCollector.Pipeline.OidMapService>();
            oidMapService.UpdateMap(config.OidMap);

            var deviceRegistry = app.Services.GetRequiredService<SnmpCollector.Pipeline.IDeviceRegistry>();
            await deviceRegistry.ReloadAsync(config.Devices);

            // Reconcile poll jobs to match the loaded device config
            var pollScheduler = app.Services.GetRequiredService<SnmpCollector.Services.DynamicPollScheduler>();
            await pollScheduler.ReconcileAsync(config.Devices, CancellationToken.None);
        }
    }
}

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
    await app.RunAsync();
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
