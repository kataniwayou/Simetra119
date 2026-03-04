using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Quartz;
using SnmpCollector.Configuration;
using SnmpCollector.Jobs;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Extensions;

/// <summary>
/// Extension methods for registering SnmpCollector services with the DI container.
/// <para>
/// Registration order in Program.cs:
/// 1. AddSnmpTelemetry  (registered first = disposed last = ForceFlush on shutdown)
/// 2. AddSnmpConfiguration
/// 3. AddSnmpScheduling
/// </para>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers OpenTelemetry MeterProvider and LoggerProvider with OTLP exporters,
    /// the custom console formatter (conditional on LoggingOptions.EnableConsole), and
    /// the log enrichment processor.
    /// <para>
    /// Must be called FIRST in DI registration: registered first = disposed last,
    /// ensuring ForceFlush runs during graceful shutdown after all other services stop.
    /// </para>
    /// <para>
    /// Phase 1: No tracing (LOG-07), no leader election, no role-gated exporters.
    /// Direct AddOtlpExporter on metrics -- all instances export.
    /// </para>
    /// </summary>
    public static IHostApplicationBuilder AddSnmpTelemetry(
        this IHostApplicationBuilder builder)
    {
        // Bind options directly from configuration -- DI container is not built yet,
        // so IOptions<T> is not available here. (Pitfall 3: do not use GetRequiredService)
        // Initialize required members with defaults; Bind() will override from config.
        var otlpOptions = new OtlpOptions { Endpoint = "", ServiceName = "snmp-collector" };
        builder.Configuration.GetSection(OtlpOptions.SectionName).Bind(otlpOptions);

        var loggingOptions = new LoggingOptions();
        builder.Configuration.GetSection(LoggingOptions.SectionName).Bind(loggingOptions);

        // Pitfall 1: Guard against blank endpoint to prevent UriFormatException during host build.
        // ValidateOnStart will surface the real validation error with a human-readable message.
        var endpoint = string.IsNullOrWhiteSpace(otlpOptions.Endpoint)
            ? "http://localhost:4317"
            : otlpOptions.Endpoint;

        // --- Metrics ---
        // No WithTracing block (LOG-07: no distributed traces in SnmpCollector).
        // No MetricRoleGatedExporter -- Phase 1 has no leader election; all instances export directly.
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: otlpOptions.ServiceName ?? "snmp-collector",
                    serviceInstanceId: Environment.GetEnvironmentVariable("HOSTNAME")
                        ?? Environment.MachineName))
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(TelemetryConstants.MeterName);
                metrics.AddRuntimeInstrumentation();
                metrics.AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri(endpoint);
                });
            });

        // --- Logging ---
        // Clear default providers (Console, Debug, EventSource) so that
        // EnableConsole=false produces zero stdout output.
        builder.Logging.ClearProviders();

        // Conditionally add custom SnmpConsoleFormatter for plain-text output.
        // Every log line is prefixed with [site|role|correlationId] for operational context.
        if (loggingOptions.EnableConsole)
        {
            builder.Logging.AddConsole(options =>
                options.FormatterName = SnmpConsoleFormatter.FormatterName);
            builder.Logging.AddConsoleFormatter<SnmpConsoleFormatter, SnmpConsoleFormatterOptions>();

            builder.Services.AddSingleton<IPostConfigureOptions<SnmpConsoleFormatterOptions>>(sp =>
                new PostConfigureSnmpFormatterOptions(sp));
        }

        // OTLP log exporter: active on ALL instances (not role-gated -- all instances export logs).
        // Enrichment processor adds site/role/correlationId to every log record.
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
            logging.SetResourceBuilder(
                ResourceBuilder.CreateDefault()
                    .AddService(
                        serviceName: otlpOptions.ServiceName ?? "snmp-collector",
                        serviceInstanceId: Environment.GetEnvironmentVariable("HOSTNAME")
                            ?? Environment.MachineName));
            logging.AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(endpoint);
            });
            logging.AddProcessor(sp =>
            {
                var siteOptions = sp.GetRequiredService<IOptions<SiteOptions>>().Value;
                var correlationService = sp.GetRequiredService<ICorrelationService>();
                return new SnmpLogEnrichmentProcessor(
                    correlationService,
                    siteOptions.Name,
                    siteOptions.Role);
            });
        });

        return builder;
    }

    /// <summary>
    /// Binds all Phase 1 options classes with ValidateDataAnnotations and ValidateOnStart
    /// for fail-fast configuration validation.
    /// <para>
    /// Phase 1 options: SiteOptions, OtlpOptions, LoggingOptions, CorrelationJobOptions.
    /// Custom IValidateOptions validators are deferred to Plan 01-05.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSnmpConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SiteOptions>()
            .Bind(configuration.GetSection(SiteOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OtlpOptions>()
            .Bind(configuration.GetSection(OtlpOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LoggingOptions>()
            .Bind(configuration.GetSection(LoggingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CorrelationJobOptions>()
            .Bind(configuration.GetSection(CorrelationJobOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers <see cref="ICorrelationService"/> as a singleton, the Quartz.NET in-memory
    /// scheduler, and <see cref="CorrelationJob"/> with a simple interval trigger.
    /// <para>
    /// ICorrelationService is registered BEFORE AddQuartz so that the DI container
    /// can resolve it when Quartz instantiates CorrelationJob.
    /// </para>
    /// <para>
    /// Phase 1 only: no heartbeat job (Phase 8), no state/metric poll jobs (Phase 6).
    /// </para>
    /// </summary>
    public static IServiceCollection AddSnmpScheduling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register ICorrelationService BEFORE AddQuartz -- CorrelationJob depends on it.
        services.AddSingleton<ICorrelationService, RotatingCorrelationService>();

        // Bind options for trigger interval
        var correlationOptions = new CorrelationJobOptions();
        configuration.GetSection(CorrelationJobOptions.SectionName).Bind(correlationOptions);

        services.AddQuartz(q =>
        {
            q.UseInMemoryStore();

            // Correlation job: rotates the global correlation ID on a fixed interval.
            // Misfire handling: NextWithRemainingCount -- skip stale fires, wait for next.
            var correlationKey = new JobKey("correlation");
            q.AddJob<CorrelationJob>(j => j.WithIdentity(correlationKey));
            q.AddTrigger(t => t
                .ForJob(correlationKey)
                .WithIdentity("correlation-trigger")
                .StartNow()
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(correlationOptions.IntervalSeconds)
                    .RepeatForever()
                    .WithMisfireHandlingInstructionNextWithRemainingCount()));
        });

        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
        });

        return services;
    }
}
