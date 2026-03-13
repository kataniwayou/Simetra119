using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SnmpCollector.Extensions;
using SnmpCollector.HealthChecks;
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
    // reloadOnChange: false -- OidMapWatcherService/DeviceWatcherService handle live reload via K8s API.
    var k8sConfig = Path.Combine(configDir, "appsettings.k8s.json");
    if (File.Exists(k8sConfig))
    {
        builder.Configuration.AddJsonFile(k8sConfig, optional: true, reloadOnChange: false);
    }

    // Phase 25: Load tenant vector configuration if present.
    // reloadOnChange: false -- tenant vector changes require restart for validation.
    var tenantVectorConfig = Path.Combine(configDir, "tenantvector.json");
    if (File.Exists(tenantVectorConfig))
    {
        builder.Configuration.AddJsonFile(tenantVectorConfig, optional: true, reloadOnChange: false);
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

// Local dev -- load OID map and devices from separate files when not in K8s.
// In K8s mode, OidMapWatcherService and DeviceWatcherService handle config
// loading via API watch on their respective ConfigMaps.
// ReconcileAsync is called here for consistency with K8s mode, where
// DeviceWatcherService calls it after ReloadAsync. PollSchedulerStartupService
// also schedules initial jobs, but ReconcileAsync is idempotent -- it will detect
// that the desired jobs already exist and make no changes.
if (!k8s.KubernetesClientConfiguration.IsInCluster())
{
    var jsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    // Load OID map from oidmaps.json (array-of-objects format)
    var oidmapsPath = Path.Combine(configDir, "oidmaps.json");
    if (File.Exists(oidmapsPath))
    {
        var oidJson = File.ReadAllText(oidmapsPath);
        var oidMapLogger = app.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SnmpCollector.Services.OidMapWatcherService>>();
        var oidMap = SnmpCollector.Services.OidMapWatcherService.ValidateAndParseOidMap(oidJson, oidMapLogger);
        if (oidMap != null)
        {
            var oidMapService = app.Services.GetRequiredService<SnmpCollector.Pipeline.OidMapService>();
            oidMapService.UpdateMap(oidMap);
        }
    }

    // Load devices from devices.json (bare array)
    var devicesPath = Path.Combine(configDir, "devices.json");
    if (File.Exists(devicesPath))
    {
        var devicesJson = File.ReadAllText(devicesPath);
        var devices = System.Text.Json.JsonSerializer.Deserialize<List<SnmpCollector.Configuration.DeviceOptions>>(devicesJson, jsonOptions);
        if (devices != null)
        {
            var deviceRegistry = app.Services.GetRequiredService<SnmpCollector.Pipeline.IDeviceRegistry>();
            await deviceRegistry.ReloadAsync(devices);

            // Reconcile poll jobs using resolved devices (IPs from registry)
            var pollScheduler = app.Services.GetRequiredService<SnmpCollector.Services.DynamicPollScheduler>();
            await pollScheduler.ReconcileAsync(deviceRegistry.AllDevices, CancellationToken.None);
        }
    }

    // Load tenant vector from tenantvector.json (section-wrapped format)
    var tenantVectorPath = Path.Combine(configDir, "tenantvector.json");
    if (File.Exists(tenantVectorPath))
    {
        var tvJson = File.ReadAllText(tenantVectorPath);
        // tenantvector.json uses IConfiguration section wrapper { "TenantVector": { "Tenants": [...] } }.
        // Extract the inner TenantVector object for direct deserialization as TenantVectorOptions.
        using var tvDoc = System.Text.Json.JsonDocument.Parse(tvJson);
        if (tvDoc.RootElement.TryGetProperty("TenantVector", out var tvElement))
        {
            var tvOptions = System.Text.Json.JsonSerializer.Deserialize<SnmpCollector.Configuration.TenantVectorOptions>(
                    tvElement.GetRawText(), jsonOptions);
            if (tvOptions != null)
            {
                var tvValidator = app.Services.GetRequiredService<SnmpCollector.Configuration.Validators.TenantVectorOptionsValidator>();
                var tvValidation = tvValidator.Validate(null, tvOptions);
                if (!tvValidation.Failed)
                {
                    var tvRegistry = app.Services.GetRequiredService<SnmpCollector.Pipeline.TenantVectorRegistry>();
                    tvRegistry.Reload(tvOptions);
                }
            }
        }
    }

    // Load command map from commandmaps.json (array-of-objects format)
    var commandmapsPath = Path.Combine(configDir, "commandmaps.json");
    if (File.Exists(commandmapsPath))
    {
        var cmdJson = File.ReadAllText(commandmapsPath);
        var cmdMapLogger = app.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SnmpCollector.Services.CommandMapWatcherService>>();
        var cmdMap = SnmpCollector.Services.CommandMapWatcherService.ValidateAndParseCommandMap(cmdJson, cmdMapLogger);
        if (cmdMap != null)
        {
            var commandMapService = app.Services.GetRequiredService<SnmpCollector.Pipeline.CommandMapService>();
            commandMapService.UpdateMap(cmdMap);
        }
    }
}

// Phase 8: Health probe endpoints with tag-filtered checks and explicit status codes.
// Each endpoint runs only the health check(s) matching its tag.
app.MapHealthChecks("/healthz/startup", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("startup"),
    ResponseWriter = HealthCheckJsonWriter.WriteResponse,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthCheckJsonWriter.WriteResponse,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = HealthCheckJsonWriter.WriteResponse,
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
