using System.Text.Json;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SnmpCollector.Configuration;
using SnmpCollector.Configuration.Validators;
using SnmpCollector.Pipeline;

namespace SnmpCollector.Services;

/// <summary>
/// Background service that watches the <c>simetra-tenants</c> ConfigMap via the Kubernetes
/// API and triggers a tenant vector reload on change. Only updates TenantVectorRegistry -- does
/// not touch DeviceRegistry or DynamicPollScheduler.
/// <para>
/// Uses the K8s watch API which sends events as the ConfigMap changes. The watch connection
/// times out after ~30 minutes (K8s server-side default), so the service reconnects
/// automatically in a loop.
/// </para>
/// <para>
/// Concurrent reload requests are serialized via <see cref="SemaphoreSlim"/> to prevent
/// race conditions when rapid successive changes arrive.
/// </para>
/// </summary>
public sealed class TenantVectorWatcherService : BackgroundService
{
    /// <summary>
    /// ConfigMap name containing the tenant vector configuration.
    /// </summary>
    internal const string ConfigMapName = "simetra-tenants";

    /// <summary>
    /// Key within the ConfigMap data that holds the tenant vector JSON document.
    /// </summary>
    internal const string ConfigKey = "tenants.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IKubernetes _kubeClient;
    private readonly TenantVectorRegistry _registry;
    private readonly TenantVectorOptionsValidator _validator;
    private readonly IOidMapService _oidMapService;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly ILogger<TenantVectorWatcherService> _logger;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private readonly string _namespace;

    public TenantVectorWatcherService(
        IKubernetes kubeClient,
        TenantVectorRegistry registry,
        TenantVectorOptionsValidator validator,
        IOidMapService oidMapService,
        IDeviceRegistry deviceRegistry,
        ILogger<TenantVectorWatcherService> logger)
    {
        _kubeClient = kubeClient ?? throw new ArgumentNullException(nameof(kubeClient));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _oidMapService = oidMapService ?? throw new ArgumentNullException(nameof(oidMapService));
        _deviceRegistry = deviceRegistry ?? throw new ArgumentNullException(nameof(deviceRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _namespace = ReadNamespace();
    }

    /// <summary>
    /// Validates, resolves IPs, and filters tenant vector options, returning a clean
    /// <see cref="TenantVectorOptions"/> containing only valid entries with resolved IPs.
    /// Invalid entries are logged as errors and excluded; the TEN-13 completeness gate
    /// skips entire tenants missing Resolved metrics, Evaluate metrics, or commands.
    /// </summary>
    /// <param name="options">Raw options from ConfigMap or local dev file.</param>
    /// <param name="oidMapService">OID map for MetricName existence checks (TEN-05).</param>
    /// <param name="deviceRegistry">Device registry for IP+Port existence checks (TEN-07) and IP resolution.</param>
    /// <param name="logger">Logger for per-entry skip and TEN-13 messages.</param>
    /// <returns>A new <see cref="TenantVectorOptions"/> containing only valid, resolved tenants.</returns>
    internal static TenantVectorOptions ValidateAndBuildTenants(
        TenantVectorOptions options,
        IOidMapService oidMapService,
        IDeviceRegistry deviceRegistry,
        ILogger logger)
    {
        var cleanTenants = new List<TenantOptions>();

        for (var i = 0; i < options.Tenants.Count; i++)
        {
            var tenantOpts = options.Tenants[i];
            var tenantId = !string.IsNullOrWhiteSpace(tenantOpts.Name)
                ? tenantOpts.Name
                : $"tenant-{i}";

            var cleanMetrics = new List<MetricSlotOptions>();
            int evaluateCount = 0;
            int resolvedCount = 0;

            // Per-metric entry validation (per-entry skip semantics).
            for (var j = 0; j < tenantOpts.Metrics.Count; j++)
            {
                var metric = tenantOpts.Metrics[j];

                // 1. Structural: empty Ip
                if (string.IsNullOrWhiteSpace(metric.Ip))
                {
                    logger.LogError(
                        "Tenant '{TenantId}' Metrics[{Index}] skipped: Ip is empty",
                        tenantId, j);
                    continue;
                }

                // 2. Structural: port out of range
                if (metric.Port < 1 || metric.Port > 65535)
                {
                    logger.LogError(
                        "Tenant '{TenantId}' Metrics[{Index}] skipped: Port {Port} is out of range [1, 65535]",
                        tenantId, j, metric.Port);
                    continue;
                }

                // 3. Structural: empty MetricName
                if (string.IsNullOrWhiteSpace(metric.MetricName))
                {
                    logger.LogError(
                        "Tenant '{TenantId}' Metrics[{Index}] skipped: MetricName is empty",
                        tenantId, j);
                    continue;
                }

                // 4. Role validation
                if (metric.Role != "Evaluate" && metric.Role != "Resolved")
                {
                    logger.LogError(
                        "Tenant '{TenantId}' Metrics[{Index}] skipped: Role '{Role}' is invalid (must be 'Evaluate' or 'Resolved')",
                        tenantId, j, metric.Role);
                    continue;
                }

                // 5. MetricName resolution (TEN-05): must exist in OID map
                if (!oidMapService.ContainsMetricName(metric.MetricName))
                {
                    logger.LogError(
                        "Tenant '{TenantId}' Metrics[{Index}] skipped: MetricName '{MetricName}' not found in OID map (TEN-05)",
                        tenantId, j, metric.MetricName);
                    continue;
                }

                // 6. IP+Port existence (TEN-07): device must be registered
                if (!deviceRegistry.TryGetByIpPort(metric.Ip, metric.Port, out var device))
                {
                    logger.LogError(
                        "Tenant '{TenantId}' Metrics[{Index}] skipped: IP+Port '{Ip}:{Port}' not found in DeviceRegistry (TEN-07)",
                        tenantId, j, metric.Ip, metric.Port);
                    continue;
                }

                // 7. Threshold: Min > Max clears threshold; metric still loads (THR-04/THR-05)
                if (metric.Threshold is { Min: not null, Max: not null } thr && thr.Min > thr.Max)
                {
                    logger.LogError(
                        "Tenant '{TenantName}' Metrics[{MetricIndex}] threshold invalid: Min {Min} > Max {Max} -- threshold cleared, metric still loads",
                        tenantId, j, thr.Min, thr.Max);
                    metric.Threshold = null;
                }

                // Resolve IntervalSeconds + GraceMultiplier from device poll group.
                var resolvedInterval = 0;
                var resolvedGrace = 2.0;
                var oid = oidMapService.ResolveToOid(metric.MetricName);
                if (oid is not null && device is not null)
                {
                    foreach (var pg in device.PollGroups)
                    {
                        if (pg.Oids.Contains(oid))
                        {
                            resolvedInterval = pg.IntervalSeconds;
                            resolvedGrace = pg.GraceMultiplier;
                            break;
                        }
                    }
                }

                // Fallback: check aggregated metrics (MetricName won't have an OID entry).
                if (resolvedInterval == 0 && device is not null)
                {
                    foreach (var pg in device.PollGroups)
                    {
                        foreach (var agg in pg.AggregatedMetrics)
                        {
                            if (string.Equals(agg.MetricName, metric.MetricName, StringComparison.OrdinalIgnoreCase))
                            {
                                resolvedInterval = pg.IntervalSeconds;
                                resolvedGrace = pg.GraceMultiplier;
                                goto resolved;
                            }
                        }
                    }
                    resolved:;
                }

                metric.IntervalSeconds = resolvedInterval;
                metric.GraceMultiplier = resolvedGrace;

                // Passed all checks — resolve IP via DeviceRegistry.AllDevices.
                var resolvedIp = metric.Ip;
                foreach (var registeredDevice in deviceRegistry.AllDevices)
                {
                    if (string.Equals(registeredDevice.ConfigAddress, metric.Ip, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogDebug("Resolved tenant metric IP {ConfigIp} -> {ResolvedIp}", metric.Ip, registeredDevice.ResolvedIp);
                        resolvedIp = registeredDevice.ResolvedIp;
                        break;
                    }
                }

                metric.Ip = resolvedIp;
                cleanMetrics.Add(metric);

                // Track role counts for TEN-13 gate.
                if (metric.Role == "Evaluate") evaluateCount++;
                else resolvedCount++;
            }

            // Per-command entry validation.
            var cleanCommands = new List<CommandSlotOptions>();
            for (var k = 0; k < tenantOpts.Commands.Count; k++)
            {
                var cmd = tenantOpts.Commands[k];

                // 1. Structural: empty Ip
                if (string.IsNullOrWhiteSpace(cmd.Ip))
                {
                    logger.LogError(
                        "Tenant '{TenantId}' Commands[{Index}] skipped: Ip is empty",
                        tenantId, k);
                    continue;
                }

                // 2. Structural: port out of range
                if (cmd.Port < 1 || cmd.Port > 65535)
                {
                    logger.LogError(
                        "Tenant '{TenantId}' Commands[{Index}] skipped: Port {Port} is out of range [1, 65535]",
                        tenantId, k, cmd.Port);
                    continue;
                }

                // 3. Structural: empty CommandName
                if (string.IsNullOrWhiteSpace(cmd.CommandName))
                {
                    logger.LogError(
                        "Tenant '{TenantId}' Commands[{Index}] skipped: CommandName is empty",
                        tenantId, k);
                    continue;
                }

                // 4. ValueType validation (TEN-03)
                if (cmd.ValueType != "Integer32" && cmd.ValueType != "IpAddress" && cmd.ValueType != "OctetString")
                {
                    logger.LogError(
                        "Tenant '{TenantId}' Commands[{Index}] skipped: ValueType '{ValueType}' is invalid (must be 'Integer32', 'IpAddress', or 'OctetString') (TEN-03)",
                        tenantId, k, cmd.ValueType);
                    continue;
                }

                // 5. Empty Value
                if (string.IsNullOrWhiteSpace(cmd.Value))
                {
                    logger.LogError(
                        "Tenant '{TenantId}' Commands[{Index}] skipped: Value is empty",
                        tenantId, k);
                    continue;
                }

                // 6. Value+ValueType parse validation: ensure Value is parseable for its declared type.
                if (cmd.ValueType == "Integer32" && !int.TryParse(cmd.Value, out _))
                {
                    logger.LogError(
                        "Tenant '{TenantId}' Commands[{Index}] skipped: Value '{Value}' is not a valid Integer32",
                        tenantId, k, cmd.Value);
                    continue;
                }

                if (cmd.ValueType == "IpAddress" && !System.Net.IPAddress.TryParse(cmd.Value, out _))
                {
                    logger.LogError(
                        "Tenant '{TenantId}' Commands[{Index}] skipped: Value '{Value}' is not a valid IpAddress",
                        tenantId, k, cmd.Value);
                    continue;
                }

                // 7. IP+Port existence (TEN-07)
                if (!deviceRegistry.TryGetByIpPort(cmd.Ip, cmd.Port, out _))
                {
                    logger.LogError(
                        "Tenant '{TenantId}' Commands[{Index}] skipped: IP+Port '{Ip}:{Port}' not found in DeviceRegistry (TEN-07)",
                        tenantId, k, cmd.Ip, cmd.Port);
                    continue;
                }

                // TEN-06: CommandName stored as-is — resolution deferred to execution time.
                logger.LogDebug(
                    "Tenant '{TenantId}' Commands[{Index}]: CommandName '{CommandName}' stored as-is (resolution deferred to execution)",
                    tenantId, k, cmd.CommandName);

                cleanCommands.Add(cmd);
            }

            // TEN-13 post-validation completeness gate.
            var missing = new List<string>(3);
            if (resolvedCount == 0) missing.Add("no Resolved metrics remaining after validation");
            if (evaluateCount == 0) missing.Add("no Evaluate metrics remaining after validation");
            if (cleanCommands.Count == 0) missing.Add("no commands remaining after validation");

            if (missing.Count > 0)
            {
                logger.LogError(
                    "Tenant '{TenantId}' skipped: {Reason}",
                    tenantId, string.Join("; ", missing));
                continue;
            }

            cleanTenants.Add(new TenantOptions
            {
                Name = tenantOpts.Name,
                Priority = tenantOpts.Priority,
                Metrics = cleanMetrics,
                Commands = cleanCommands
            });
        }

        return new TenantVectorOptions { Tenants = cleanTenants };
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial load: read current ConfigMap state before starting watch
        try
        {
            await LoadFromConfigMapAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation(
                "TenantVectorWatcher initial load complete for {ConfigMap}/{Key} in namespace {Namespace}",
                ConfigMapName, ConfigKey, _namespace);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "TenantVectorWatcher initial load failed for {ConfigMap}/{Key} -- will retry via watch loop",
                ConfigMapName, ConfigKey);
        }

        // Watch loop with automatic reconnect (K8s watch timeout ~30 min)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug(
                    "TenantVectorWatcher starting watch on {ConfigMap} in namespace {Namespace}",
                    ConfigMapName, _namespace);

                var response = _kubeClient.CoreV1.ListNamespacedConfigMapWithHttpMessagesAsync(
                    namespaceParameter: _namespace,
                    fieldSelector: $"metadata.name={ConfigMapName}",
                    watch: true,
                    cancellationToken: stoppingToken);

                // CS0618: WatchAsync overload is marked obsolete but no IAsyncEnumerable replacement exists in KubernetesClient 18.x
#pragma warning disable CS0618
                await foreach (var (eventType, configMap) in response.WatchAsync<V1ConfigMap, V1ConfigMapList>(
                    cancellationToken: stoppingToken).ConfigureAwait(false))
#pragma warning restore CS0618
                {
                    if (eventType is WatchEventType.Added or WatchEventType.Modified)
                    {
                        _logger.LogInformation(
                            "TenantVectorWatcher received {EventType} event for {ConfigMap}",
                            eventType, ConfigMapName);

                        await HandleConfigMapChangedAsync(configMap, stoppingToken).ConfigureAwait(false);
                    }
                    else if (eventType is WatchEventType.Deleted)
                    {
                        _logger.LogWarning(
                            "ConfigMap {ConfigMap} was deleted -- skipping reload, retaining current tenant vector config",
                            ConfigMapName);
                    }
                }

                // Watch ended normally (server closed the connection after ~30 min)
                _logger.LogDebug("TenantVectorWatcher watch connection closed, reconnecting");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown -- exit the loop
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "TenantVectorWatcher watch disconnected unexpectedly, reconnecting in 5s");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("TenantVectorWatcher stopped");
    }

    /// <summary>
    /// Reads the ConfigMap directly (non-watch) and applies configuration.
    /// Used for initial load before the watch loop starts.
    /// </summary>
    private async Task LoadFromConfigMapAsync(CancellationToken ct)
    {
        var configMap = await _kubeClient.CoreV1.ReadNamespacedConfigMapAsync(
            ConfigMapName, _namespace, cancellationToken: ct).ConfigureAwait(false);

        await HandleConfigMapChangedAsync(configMap, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Parses the tenant vector JSON from the ConfigMap, validates it, and reloads
    /// <see cref="TenantVectorRegistry"/> if validation passes.
    /// </summary>
    private async Task HandleConfigMapChangedAsync(V1ConfigMap configMap, CancellationToken ct)
    {
        if (configMap.Data is null || !configMap.Data.TryGetValue(ConfigKey, out var jsonContent))
        {
            _logger.LogWarning(
                "ConfigMap {ConfigMap} does not contain key {ConfigKey} -- skipping reload",
                ConfigMapName, ConfigKey);
            return;
        }

        TenantVectorOptions? options;
        try
        {
            // Bare array format: deserialize as List<TenantOptions>, then wrap.
            var tenantList = JsonSerializer.Deserialize<List<TenantOptions>>(jsonContent, JsonOptions);
            options = tenantList is not null ? new TenantVectorOptions { Tenants = tenantList } : null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "Failed to parse {ConfigKey} from ConfigMap {ConfigMap} -- skipping reload",
                ConfigKey, ConfigMapName);
            return;
        }

        if (options is null)
        {
            _logger.LogWarning(
                "Deserialized {ConfigKey} is null -- skipping reload",
                ConfigKey);
            return;
        }

        // Validate before reloading -- invalid config is logged as Error and previous config retained
        var validationResult = _validator.Validate(null, options);
        if (validationResult.Failed)
        {
            _logger.LogError(
                "TenantVector config validation failed for {ConfigMap}/{Key} -- skipping reload. Failures: {Failures}",
                ConfigMapName, ConfigKey, string.Join("; ", validationResult.Failures));
            return;
        }

        await _reloadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cleanOptions = ValidateAndBuildTenants(options, _oidMapService, _deviceRegistry, _logger);
            _registry.Reload(cleanOptions);

            _logger.LogInformation(
                "Tenant vector reload complete for {ConfigMap}/{Key}",
                ConfigMapName, ConfigKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tenant vector reload failed -- previous config remains active");
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    /// <summary>
    /// Reads the Kubernetes namespace from the service account mount point.
    /// Falls back to "simetra" if the file is not available (local dev).
    /// </summary>
    private static string ReadNamespace()
    {
        const string namespacePath = "/var/run/secrets/kubernetes.io/serviceaccount/namespace";
        try
        {
            if (File.Exists(namespacePath))
                return File.ReadAllText(namespacePath).Trim();
        }
        catch
        {
            // Fall through to default
        }

        return "simetra";
    }
}
