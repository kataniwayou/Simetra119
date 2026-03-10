using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Quartz;
using SnmpCollector.Configuration;
using SnmpCollector.Configuration.Validators;
using SnmpCollector.HealthChecks;
using SnmpCollector.Jobs;
using SnmpCollector.Lifecycle;
using SnmpCollector.Pipeline;
using SnmpCollector.Pipeline.Behaviors;
using SnmpCollector.Services;
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
/// 5. AddSnmpHealthChecks (startup, readiness, liveness probes)
/// 6. AddSnmpLifecycle    (GracefulShutdownService -- MUST BE LAST, stops FIRST)
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
    /// Phase 7: MetricRoleGatedExporter wraps OtlpMetricExporter to gate business metrics
    /// (LeaderMeterName) behind ILeaderElection.IsLeader. Pipeline and System.Runtime metrics
    /// are exported by all instances. No tracing (LOG-07).
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
        var podName = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: otlpOptions.ServiceName ?? "snmp-collector",
                    serviceInstanceId: Environment.GetEnvironmentVariable("PHYSICAL_HOSTNAME")
                        ?? Environment.MachineName)
                .AddAttributes([
                    new KeyValuePair<string, object>("k8s.pod.name", podName)
                ]))
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(TelemetryConstants.MeterName);        // Pipeline metrics (always exported)
                metrics.AddMeter(TelemetryConstants.LeaderMeterName);  // Business metrics (leader-gated)
                metrics.AddRuntimeInstrumentation();                   // System.Runtime (always exported)

                // Manual construction required: AddOtlpExporter() creates the exporter internally
                // and prevents wrapping. MetricRoleGatedExporter wraps OtlpMetricExporter to gate
                // business metrics (LeaderMeterName) behind ILeaderElection.IsLeader.
                metrics.AddReader(sp =>
                {
                    var leaderElection = sp.GetRequiredService<ILeaderElection>();
                    var otlpExporter = new OtlpMetricExporter(new OtlpExporterOptions
                    {
                        Endpoint = new Uri(endpoint)
                    });
                    var roleGated = new MetricRoleGatedExporter(
                        otlpExporter, leaderElection, TelemetryConstants.LeaderMeterName);
                    // Cumulative temporality required: Prometheus rate() needs monotonically
                    // increasing counter values. Delta temporality sends per-interval deltas
                    // which rate() cannot compute over.
                    return new PeriodicExportingMetricReader(roleGated, exportIntervalMilliseconds: 15_000)
                    {
                        TemporalityPreference = MetricReaderTemporalityPreference.Cumulative
                    };
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
                        serviceInstanceId: Environment.GetEnvironmentVariable("PHYSICAL_HOSTNAME")
                            ?? Environment.MachineName)
                    .AddAttributes([
                        new KeyValuePair<string, object>("k8s.pod.name", podName)
                    ]));
            logging.AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(endpoint);
            });
            logging.AddProcessor(sp =>
            {
                var hostName = Environment.GetEnvironmentVariable("PHYSICAL_HOSTNAME") ?? Environment.MachineName;
                var correlationService = sp.GetRequiredService<ICorrelationService>();
                var leaderElection = sp.GetRequiredService<ILeaderElection>();
                return new SnmpLogEnrichmentProcessor(
                    correlationService,
                    hostName,
                    () => leaderElection.CurrentRole);
            });
        });

        return builder;
    }

    /// <summary>
    /// Binds all Phase 1 and Phase 2 options classes with fail-fast configuration validation,
    /// and registers Phase 2 pipeline singletons (DeviceRegistry, OidMapService).
    /// <para>
    /// Phase 1 options: PodIdentityOptions, OtlpOptions, LoggingOptions, CorrelationJobOptions.
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
        services.AddOptions<PodIdentityOptions>()
            .Bind(configuration.GetSection(PodIdentityOptions.SectionName))
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

        services.AddOptions<HeartbeatJobOptions>()
            .Bind(configuration.GetSection(HeartbeatJobOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Custom IValidateOptions for cross-field validation (Phase 1)
        services.AddSingleton<IValidateOptions<PodIdentityOptions>, PodIdentityOptionsValidator>();
        services.AddSingleton<IValidateOptions<OtlpOptions>, OtlpOptionsValidator>();

        // --- Phase 7: Leader election ---
        // K8s environment detection: IsInCluster() checks KUBERNETES_SERVICE_HOST +
        // KUBERNETES_SERVICE_PORT env vars AND service account token/cert files.
        if (k8s.KubernetesClientConfiguration.IsInCluster())
        {
            // LeaseOptions: only needed in K8s (not bound for local dev).
            services.AddOptions<LeaseOptions>()
                .Bind(configuration.GetSection(LeaseOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<LeaseOptions>, LeaseOptionsValidator>();

            var kubeConfig = k8s.KubernetesClientConfiguration.InClusterConfig();
            services.AddSingleton<k8s.IKubernetes>(new k8s.Kubernetes(kubeConfig));

            // CRITICAL: Register concrete type FIRST, then resolve same instance for both interfaces.
            // If AddSingleton<ILeaderElection, K8sLeaseElection>() and AddHostedService<K8sLeaseElection>()
            // are used separately, DI creates TWO instances -- the hosted service updates _isLeader on one,
            // but ILeaderElection consumers read from a different instance that never becomes leader.
            services.AddSingleton<K8sLeaseElection>();
            services.AddSingleton<ILeaderElection>(sp => sp.GetRequiredService<K8sLeaseElection>());
            services.AddHostedService(sp => sp.GetRequiredService<K8sLeaseElection>());

            // Phase 15: Independent ConfigMap watchers for OID maps and device config
            services.AddSingleton<OidMapWatcherService>();
            services.AddHostedService(sp => sp.GetRequiredService<OidMapWatcherService>());

            services.AddSingleton<DeviceWatcherService>();
            services.AddHostedService(sp => sp.GetRequiredService<DeviceWatcherService>());

            // Phase 28: Tenant vector ConfigMap watcher
            services.AddSingleton<TenantVectorWatcherService>();
            services.AddHostedService(sp => sp.GetRequiredService<TenantVectorWatcherService>());
        }
        else
        {
            // Local dev: AlwaysLeaderElection (IsLeader=true, no K8s dependency).
            services.AddSingleton<ILeaderElection, AlwaysLeaderElection>();
        }

        // Phase 7: PodIdentityOptions.PodIdentity defaults to HOSTNAME env var (K8s pod name),
        // falling back to machine name for local dev.
        services.PostConfigure<PodIdentityOptions>(options =>
        {
            options.PodIdentity ??= Environment.GetEnvironmentVariable("HOSTNAME")
                ?? Environment.MachineName;
        });

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

        // --- Phase 8: Liveness configuration ---
        services.AddOptions<LivenessOptions>()
            .Bind(configuration.GetSection(LivenessOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // --- Phase 5: Channel configuration ---
        services.AddOptions<ChannelsOptions>()
            .Bind(configuration.GetSection(ChannelsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // --- Phase 25: Tenant vector configuration ---
        services.AddOptions<TenantVectorOptions>()
            .Bind(configuration.GetSection(TenantVectorOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<TenantVectorOptionsValidator>();
        services.AddSingleton<IValidateOptions<TenantVectorOptions>>(
            sp => sp.GetRequiredService<TenantVectorOptionsValidator>());

        // --- Phase 26: Tenant vector registry ---
        services.AddSingleton<TenantVectorRegistry>(sp =>
            new TenantVectorRegistry(sp.GetRequiredService<ILogger<TenantVectorRegistry>>()));
        services.AddSingleton<ITenantVectorRegistry>(sp => sp.GetRequiredService<TenantVectorRegistry>());

        // --- Phase 2 pipeline singletons ---
        // DeviceRegistry depends on IOptions<DevicesOptions> for initial startup construction.
        // In K8s mode, DeviceWatcherService calls ReloadAsync to overwrite with ConfigMap data.
        // In local dev mode, Program.cs calls ReloadAsync after build with devices.json data.
        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();

        // OidMapService: initial empty map. In K8s mode, OidMapWatcherService populates it.
        // In local dev mode, populated after DI build from oidmaps.json.
        services.AddSingleton<OidMapService>(sp =>
            new OidMapService(new Dictionary<string, string>(), sp.GetRequiredService<ILogger<OidMapService>>()));
        services.AddSingleton<IOidMapService>(sp => sp.GetRequiredService<OidMapService>());

        // Phase 15: DynamicPollScheduler available in both K8s and local dev modes.
        // K8s: called by DeviceWatcherService. Local dev: called by Program.cs after build.
        services.AddSingleton<DynamicPollScheduler>();

        // --- Phase 2: Cardinality audit (runs during StartingAsync, before Quartz starts) ---
        // IHostedLifecycleService.StartingAsync fires before IHostedService.StartAsync,
        // so the audit completes before the Quartz scheduler begins executing jobs.
        services.AddHostedService<CardinalityAuditService>();

        return services;
    }

    /// <summary>
    /// Registers the MediatR pipeline for SNMP OID processing.
    /// <para>
    /// SnmpOidReceived implements IRequest&lt;Unit&gt; (not INotification) so that the registered
    /// IPipelineBehavior chain executes on every ISender.Send call. INotification + IPublisher.Publish
    /// does NOT invoke IPipelineBehavior in MediatR 12.x -- only IRequest&lt;T&gt; + ISender.Send does.
    /// </para>
    /// <para>
    /// Behavior registration order (first registered = outermost = runs first):
    /// 1. <see cref="LoggingBehavior{TRequest,TResponse}"/>              — outermost, logs entry/exit
    /// 2. <see cref="ExceptionBehavior{TRequest,TResponse}"/>            — catches unhandled exceptions
    /// 3. <see cref="ValidationBehavior{TRequest,TResponse}"/>           — validates message and device
    /// 4. <see cref="OidResolutionBehavior{TRequest,TResponse}"/>        — resolves OID to metric name
    /// 5. <see cref="ValueExtractionBehavior{TRequest,TResponse}"/>      — extracts numeric/string value once
    /// 6. <see cref="TenantVectorFanOutBehavior{TRequest,TResponse}"/>   — routes to tenant vector slots
    /// </para>
    /// </summary>
    public static IServiceCollection AddSnmpPipeline(
        this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<SnmpOidReceived>();

            // Behavior order: first registered = outermost = runs first in pipeline.
            // Behaviors fire because SnmpOidReceived : IRequest<Unit> dispatched via ISender.Send.
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));              // 1st = outermost
            cfg.AddOpenBehavior(typeof(ExceptionBehavior<,>));          // 2nd
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));         // 3rd
            cfg.AddOpenBehavior(typeof(OidResolutionBehavior<,>));      // 4th
            cfg.AddOpenBehavior(typeof(ValueExtractionBehavior<,>));    // 5th
            cfg.AddOpenBehavior(typeof(TenantVectorFanOutBehavior<,>)); // 6th — Phase 27
        });

        // Pipeline telemetry: metrics for pipeline latency, handled/rejected counts.
        services.AddSingleton<PipelineMetricService>();

        // SNMP client: wraps static Messenger.GetAsync for testability.
        services.AddSingleton<ISnmpClient, SharpSnmpClient>();

        // SNMP instrument factory: ConcurrentDictionary cache for snmp_gauge and snmp_info instruments.
        services.AddSingleton<ISnmpMetricFactory, SnmpMetricFactory>();

        // --- Phase 10: Single shared trap channel (replaces per-device IDeviceChannelManager) ---
        // Registration order: channel singleton before hosted services.
        services.AddSingleton<ITrapChannel, TrapChannel>();

        services.AddHostedService<SnmpTrapListenerService>();
        services.AddHostedService<ChannelConsumerService>();

        // Phase 8: Liveness vector for job completion timestamp tracking.
        // Singleton stamped by every job's finally block, read by LivenessHealthCheck.
        services.AddSingleton<ILivenessVectorService, LivenessVectorService>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="ICorrelationService"/> and <see cref="IDeviceUnreachabilityTracker"/>
    /// as singletons, the Quartz.NET in-memory scheduler with auto-scaled thread pool,
    /// <see cref="CorrelationJob"/> with a simple interval trigger, <see cref="HeartbeatJob"/>
    /// with a configurable interval, and one <see cref="MetricPollJob"/> per device/poll-group
    /// pair with correct JobDataMap.
    /// <para>
    /// ICorrelationService is registered BEFORE AddQuartz so that the DI container
    /// can resolve it when Quartz instantiates CorrelationJob.
    /// </para>
    /// <para>
    /// Thread pool: maxConcurrency = 1 (CorrelationJob) + sum of poll groups across all devices.
    /// Ensures every job gets a thread immediately without waiting for pool expansion.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSnmpScheduling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Phase 6: Per-device consecutive failure tracking for unreachability detection.
        services.AddSingleton<IDeviceUnreachabilityTracker, DeviceUnreachabilityTracker>();

        // Register ICorrelationService BEFORE AddQuartz -- CorrelationJob depends on it.
        services.AddSingleton<ICorrelationService, RotatingCorrelationService>();

        // Bind options for trigger intervals.
        var correlationOptions = new CorrelationJobOptions();
        configuration.GetSection(CorrelationJobOptions.SectionName).Bind(correlationOptions);

        var heartbeatOptions = new HeartbeatJobOptions();
        configuration.GetSection(HeartbeatJobOptions.SectionName).Bind(heartbeatOptions);

        // Phase 6: Bind DevicesOptions to calculate thread pool size and register poll jobs.
        // CRITICAL: bind directly into .Devices (not the wrapper) -- matches AddSnmpConfiguration pattern.
        // DI container is NOT built yet; IOptions<DevicesOptions> is not available here.
        var devicesOptions = new DevicesOptions();
        configuration.GetSection(DevicesOptions.SectionName).Bind(devicesOptions.Devices);

        // Phase 8: Job interval registry for liveness staleness threshold calculation.
        // Populated here during Quartz configuration, then registered as singleton.
        var intervalRegistry = new JobIntervalRegistry();

        // Thread pool: generous ceiling to accommodate dynamic device additions at runtime.
        // Static jobs (CorrelationJob + HeartbeatJob) = 2, plus headroom for poll jobs.
        var initialJobCount = 2; // CorrelationJob + HeartbeatJob
        foreach (var device in devicesOptions.Devices)
            initialJobCount += device.MetricPolls.Count;
        var threadPoolSize = Math.Max(initialJobCount, 50);

        services.AddQuartz(q =>
        {
            q.UseInMemoryStore();

            // Thread pool with generous ceiling: accommodates dynamic device additions at runtime
            // without needing to resize. Initial jobs get threads immediately; headroom for
            // DynamicPollScheduler to add new metric-poll jobs via ConfigMap reload.
            q.UseDefaultThreadPool(maxConcurrency: threadPoolSize);

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

            intervalRegistry.Register("correlation", correlationOptions.IntervalSeconds);

            // HeartbeatJob: sends loopback SNMP trap to prove scheduler + pipeline alive.
            var heartbeatKey = new JobKey("heartbeat");
            q.AddJob<HeartbeatJob>(j => j.WithIdentity(heartbeatKey));
            q.AddTrigger(t => t
                .ForJob(heartbeatKey)
                .WithIdentity("heartbeat-trigger")
                .StartNow()
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(heartbeatOptions.IntervalSeconds)
                    .RepeatForever()
                    .WithMisfireHandlingInstructionNextWithRemainingCount()));

            intervalRegistry.Register("heartbeat", heartbeatOptions.IntervalSeconds);

            // Phase 6: MetricPollJob per device per poll group.
            // Job keys use raw config address (DNS or IP) so operators can correlate to ConfigMap.
            // for loops (not foreach) to avoid lambda variable capture bug (Pitfall 8).
            for (var di = 0; di < devicesOptions.Devices.Count; di++)
            {
                var device = devicesOptions.Devices[di];
                for (var pi = 0; pi < device.MetricPolls.Count; pi++)
                {
                    var poll = device.MetricPolls[pi];
                    var jobKey = new JobKey($"metric-poll-{device.IpAddress}_{device.Port}-{pi}");
                    q.AddJob<MetricPollJob>(j => j
                        .WithIdentity(jobKey)
                        .UsingJobData("configAddress", device.IpAddress)
                        .UsingJobData("port", device.Port)
                        .UsingJobData("pollIndex", pi)
                        .UsingJobData("intervalSeconds", poll.IntervalSeconds));

                    q.AddTrigger(t => t
                        .ForJob(jobKey)
                        .WithIdentity($"metric-poll-{device.IpAddress}_{device.Port}-{pi}-trigger")
                        .StartNow()
                        .WithSimpleSchedule(s => s
                            .WithIntervalInSeconds(poll.IntervalSeconds)
                            .RepeatForever()
                            .WithMisfireHandlingInstructionNextWithRemainingCount()));

                    intervalRegistry.Register($"metric-poll-{device.IpAddress}_{device.Port}-{pi}", poll.IntervalSeconds);
                }
            }
        });

        services.AddSingleton<IJobIntervalRegistry>(intervalRegistry);

        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
        });

        // Phase 6: Log job registration summary at startup (poll job count, device count, thread pool size).
        services.AddHostedService<PollSchedulerStartupService>();

        return services;
    }

    /// <summary>
    /// Registers health checks for K8s probe endpoints.
    /// <para>
    /// Three health checks with distinct tags:
    /// - startup: OID map loaded and poll definitions registered (HLTH-01)
    /// - ready: trap listener running and device registry populated (HLTH-02)
    /// - live: per-job staleness detection via liveness vector (HLTH-03)
    /// </para>
    /// </summary>
    public static IServiceCollection AddSnmpHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<StartupHealthCheck>("startup", tags: new[] { "startup" })
            .AddCheck<ReadinessHealthCheck>("readiness", tags: new[] { "ready" })
            .AddCheck<LivenessHealthCheck>("liveness", tags: new[] { "live" });

        return services;
    }

    /// <summary>
    /// Registers <see cref="GracefulShutdownService"/> and configures <see cref="HostOptions.ShutdownTimeout"/>.
    /// <para>
    /// MUST be called LAST in DI registration order (SHUT-01). The .NET Generic Host stops
    /// <see cref="IHostedService"/> instances in REVERSE registration order, so the
    /// last-registered service stops first.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSnmpLifecycle(this IServiceCollection services)
    {
        // SHUT-08: Total shutdown timeout 30 seconds
        services.Configure<HostOptions>(opts =>
            opts.ShutdownTimeout = TimeSpan.FromSeconds(30));

        // SHUT-01: MUST BE LAST -- registered last = stops first
        services.AddHostedService<GracefulShutdownService>();

        return services;
    }
}
