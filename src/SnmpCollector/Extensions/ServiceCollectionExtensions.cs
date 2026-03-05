using MediatR;
using MediatR.NotificationPublishers;
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
using SnmpCollector.Configuration.Validators;
using SnmpCollector.Jobs;
using SnmpCollector.Pipeline;
using SnmpCollector.Pipeline.Behaviors;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Extensions;

/// <summary>
/// Extension methods for registering SnmpCollector services with the DI container.
/// <para>
/// Registration order in Program.cs:
/// 1. AddSnmpTelemetry    (registered first = disposed last = ForceFlush on shutdown)
/// 2. AddSnmpConfiguration
/// 3. AddSnmpPipeline     (MediatR + behaviors; depends on Phase 2 registrations)
/// 4. AddSnmpScheduling
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
    /// Binds all Phase 1 and Phase 2 options classes with fail-fast configuration validation,
    /// and registers Phase 2 pipeline singletons (DeviceRegistry, OidMapService).
    /// <para>
    /// Phase 1 options: SiteOptions, OtlpOptions, LoggingOptions, CorrelationJobOptions.
    /// Phase 2 options: DevicesOptions, SnmpListenerOptions, OidMapOptions.
    /// Phase 2 services: IDeviceRegistry (DeviceRegistry), IOidMapService (OidMapService).
    /// </para>
    /// <para>
    /// DevicesOptions uses Configure&lt;IConfiguration&gt; delegate binding because the JSON
    /// "Devices" key is a top-level array, not an object -- standard .Bind() cannot map
    /// array children to a POCO with a named list property.
    /// OidMapOptions uses the same pattern: "OidMap" key is a flat JSON object of OID->name
    /// pairs that must be bound into <see cref="OidMapOptions.Entries"/> directly.
    /// Both are registered without ValidateOnStart (DevicesOptions: empty is valid;
    /// OidMapOptions: empty map is valid -- unknown OIDs resolve to "Unknown").
    /// </para>
    /// </summary>
    public static IServiceCollection AddSnmpConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- Phase 1 options ---
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

        // Custom IValidateOptions for cross-field validation (Phase 1)
        services.AddSingleton<IValidateOptions<SiteOptions>, SiteOptionsValidator>();
        services.AddSingleton<IValidateOptions<OtlpOptions>, OtlpOptionsValidator>();

        // --- Phase 2 options ---
        // DevicesOptions: "Devices" is a JSON array; bind list directly into the Devices property.
        // Empty device list is valid (pod with no poll targets still receives traps).
        // ValidateOnStart enabled: DevicesOptionsValidator fires at startup to catch config errors early.
        services.AddOptions<DevicesOptions>()
            .Configure<IConfiguration>((opts, config) =>
                config.GetSection(DevicesOptions.SectionName).Bind(opts.Devices))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<DevicesOptions>, DevicesOptionsValidator>();

        // SnmpListenerOptions: standard object section with DataAnnotations on all fields.
        services.AddOptions<SnmpListenerOptions>()
            .Bind(configuration.GetSection(SnmpListenerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<SnmpListenerOptions>, SnmpListenerOptionsValidator>();

        // OidMapOptions: "OidMap" is a flat JSON object of OID->name pairs.
        // Bind the section directly into Entries dictionary. No ValidateOnStart -- empty map is valid.
        services.AddOptions<OidMapOptions>()
            .Configure<IConfiguration>((opts, config) =>
                config.GetSection(OidMapOptions.SectionName).Bind(opts.Entries));

        // --- Phase 2 pipeline singletons ---
        // DeviceRegistry depends on IOptions<DevicesOptions> and IOptions<SnmpListenerOptions>.
        // OidMapService depends on IOptionsMonitor<OidMapOptions> for hot-reload on appsettings change.
        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
        services.AddSingleton<IOidMapService, OidMapService>();

        // --- Phase 2: Cardinality audit (runs during StartingAsync, before Quartz starts) ---
        // IHostedLifecycleService.StartingAsync fires before IHostedService.StartAsync,
        // so the audit completes before the Quartz scheduler begins executing jobs.
        services.AddHostedService<CardinalityAuditService>();

        return services;
    }

    /// <summary>
    /// Registers the MediatR pipeline for SNMP OID processing.
    /// <para>
    /// Behavior registration order (first registered = outermost = runs first):
    /// 1. <see cref="LoggingBehavior{TRequest,TResponse}"/>     — outermost, logs entry/exit
    /// 2. <see cref="ExceptionBehavior{TRequest,TResponse}"/>   — catches unhandled exceptions
    /// 3. <see cref="ValidationBehavior{TRequest,TResponse}"/>  — validates message and device
    /// 4. <see cref="OidResolutionBehavior{TRequest,TResponse}"/> — innermost, resolves OID to metric name
    /// </para>
    /// <para>
    /// <see cref="TaskWhenAllPublisher"/> is set as a singleton instance on the MediatR config
    /// (PIPE-09) — notification handlers run concurrently via Task.WhenAll.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSnmpPipeline(
        this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<SnmpOidReceived>();

            // PIPE-09: singleton instance (not type registration) so the publisher is
            // shared across all notification dispatches without repeated allocation.
            cfg.NotificationPublisher = new TaskWhenAllPublisher();

            // Behavior order: first registered = outermost = runs first in pipeline.
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));       // 1st = outermost
            cfg.AddOpenBehavior(typeof(ExceptionBehavior<,>));     // 2nd
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));    // 3rd
            cfg.AddOpenBehavior(typeof(OidResolutionBehavior<,>)); // 4th = innermost
        });

        // Pipeline telemetry: metrics for pipeline latency, handled/rejected counts.
        services.AddSingleton<PipelineMetricService>();

        // SNMP instrument factory: ConcurrentDictionary cache for snmp_gauge and snmp_info instruments.
        services.AddSingleton<ISnmpMetricFactory, SnmpMetricFactory>();

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
