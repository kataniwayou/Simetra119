using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;

namespace SnmpCollector.Services;

/// <summary>
/// Background service that watches the <c>simetra-devices</c> ConfigMap via the Kubernetes
/// API and triggers a device registry reload and poll job reconciliation on change.
/// Only updates DeviceRegistry and DynamicPollScheduler -- does not touch OidMapService.
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
public sealed class DeviceWatcherService : BackgroundService
{
    /// <summary>
    /// ConfigMap name containing device definitions.
    /// </summary>
    internal const string ConfigMapName = "simetra-devices";

    /// <summary>
    /// Key within the ConfigMap data that holds the devices JSON array.
    /// </summary>
    internal const string ConfigKey = "devices.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IKubernetes _kubeClient;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly DynamicPollScheduler _pollScheduler;
    private readonly IOidMapService _oidMapService;
    private readonly ILogger<DeviceWatcherService> _logger;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private readonly string _namespace;

    public DeviceWatcherService(
        IKubernetes kubeClient,
        IDeviceRegistry deviceRegistry,
        DynamicPollScheduler pollScheduler,
        IOidMapService oidMapService,
        ILogger<DeviceWatcherService> logger)
    {
        _kubeClient = kubeClient ?? throw new ArgumentNullException(nameof(kubeClient));
        _deviceRegistry = deviceRegistry ?? throw new ArgumentNullException(nameof(deviceRegistry));
        _pollScheduler = pollScheduler ?? throw new ArgumentNullException(nameof(pollScheduler));
        _oidMapService = oidMapService ?? throw new ArgumentNullException(nameof(oidMapService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _namespace = ReadNamespace();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial load: read current ConfigMap state before starting watch
        try
        {
            await LoadFromConfigMapAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation(
                "DeviceWatcher initial load complete for {ConfigMap}/{Key} in namespace {Namespace}",
                ConfigMapName, ConfigKey, _namespace);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "DeviceWatcher initial load failed for {ConfigMap}/{Key} -- will retry via watch loop",
                ConfigMapName, ConfigKey);
        }

        // Watch loop with automatic reconnect (K8s watch timeout ~30 min)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug(
                    "DeviceWatcher starting watch on {ConfigMap} in namespace {Namespace}",
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
                            "DeviceWatcher received {EventType} event for {ConfigMap}",
                            eventType, ConfigMapName);

                        await HandleConfigMapChangedAsync(configMap, stoppingToken).ConfigureAwait(false);
                    }
                    else if (eventType is WatchEventType.Deleted)
                    {
                        _logger.LogWarning(
                            "ConfigMap {ConfigMap} was deleted -- skipping reload, retaining current devices",
                            ConfigMapName);
                    }
                }

                // Watch ended normally (server closed the connection after ~30 min)
                _logger.LogDebug("DeviceWatcher watch connection closed, reconnecting");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown -- exit the loop
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "DeviceWatcher watch disconnected unexpectedly, reconnecting in 5s");

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

        _logger.LogInformation("DeviceWatcher stopped");
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
    /// Parses the devices JSON array from the ConfigMap, validates and builds
    /// <see cref="DeviceInfo"/> objects via <see cref="ValidateAndBuildDevicesAsync"/>,
    /// then applies the new device list to <see cref="IDeviceRegistry"/> and
    /// <see cref="DynamicPollScheduler"/>.
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

        List<DeviceOptions>? devices;
        try
        {
            devices = JsonSerializer.Deserialize<List<DeviceOptions>>(jsonContent, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "Failed to parse {ConfigKey} from ConfigMap {ConfigMap} -- skipping reload",
                ConfigKey, ConfigMapName);
            return;
        }

        if (devices is null)
        {
            _logger.LogWarning(
                "Deserialized {ConfigKey} is null -- skipping reload",
                ConfigKey);
            return;
        }

        await _reloadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 1. Validate and build DeviceInfo objects (DNS resolution, CS extraction, OID resolution)
            var deviceInfos = await ValidateAndBuildDevicesAsync(devices, _oidMapService, _logger, ct).ConfigureAwait(false);

            // 2. Reload device registry (pure store operation)
            await _deviceRegistry.ReloadAsync(deviceInfos).ConfigureAwait(false);

            // 3. Reconcile Quartz poll jobs using resolved devices (IPs from registry)
            await _pollScheduler.ReconcileAsync(_deviceRegistry.AllDevices, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Device reload complete: {DeviceCount} devices",
                deviceInfos.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Device reload failed -- previous config remains active");
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    /// <summary>
    /// Validates <paramref name="devices"/> and builds a list of <see cref="DeviceInfo"/> objects.
    /// Performs CommunityString extraction, async DNS resolution, OID resolution via
    /// <paramref name="oidMapService"/>, and duplicate IP+Port / CommunityString detection.
    /// Invalid entries are logged and skipped; the returned list contains only valid devices.
    /// </summary>
    /// <param name="devices">Raw device options to validate and resolve.</param>
    /// <param name="oidMapService">Service for resolving metric names to OID strings.</param>
    /// <param name="logger">Logger for structured validation output.</param>
    /// <param name="ct">Cancellation token for async DNS resolution.</param>
    /// <returns>List of validated, DNS-resolved, poll-group-built <see cref="DeviceInfo"/> objects.</returns>
    internal static async Task<List<DeviceInfo>> ValidateAndBuildDevicesAsync(
        List<DeviceOptions> devices,
        IOidMapService oidMapService,
        ILogger logger,
        CancellationToken ct)
    {
        var byIpPortSeen = new Dictionary<string, DeviceInfo>(StringComparer.OrdinalIgnoreCase);
        var seenCommunityStrings = new Dictionary<string, string>(StringComparer.Ordinal);
        var result = new List<DeviceInfo>(devices.Count);

        foreach (var d in devices)
        {
            // 1. CommunityString extraction
            if (!CommunityStringHelper.TryExtractDeviceName(d.CommunityString, out var deviceName))
            {
                logger.LogError(
                    "Device at {IpAddress}:{Port} has invalid CommunityString '{CommunityString}' -- skipping",
                    d.IpAddress, d.Port, d.CommunityString);
                continue;
            }

            // 2. DNS resolution (async)
            IPAddress ip;
            if (IPAddress.TryParse(d.IpAddress, out var parsed))
            {
                ip = parsed.MapToIPv4();
            }
            else
            {
                // Async DNS resolution for K8s Service names
                var addresses = await Dns.GetHostAddressesAsync(d.IpAddress, ct).ConfigureAwait(false);
                ip = addresses.First(a => a.AddressFamily == AddressFamily.InterNetwork);
            }

            // 3. BuildPollGroups (OID resolution)
            var pollGroups = BuildPollGroups(d.Polls, deviceName, oidMapService, logger);

            // 4. Duplicate IP+Port check
            var ipPortKey = $"{d.IpAddress}:{d.Port}";
            if (byIpPortSeen.TryGetValue(ipPortKey, out var existing))
            {
                logger.LogError(
                    "Device at {IpAddress}:{Port} (CommunityString '{CommunityString}') is a duplicate of existing device '{ExistingName}' -- skipping",
                    d.IpAddress, d.Port, d.CommunityString, existing.Name);
                continue;
            }

            // 5. Duplicate CommunityString warning (different IP+Port)
            if (seenCommunityStrings.TryGetValue(d.CommunityString, out var priorName))
            {
                logger.LogWarning(
                    "Device '{DeviceName}' at {IpAddress}:{Port} has CommunityString '{CommunityString}' also used by device '{PriorDevice}' -- both loaded (different IP+Port)",
                    deviceName, d.IpAddress, d.Port, d.CommunityString, priorName);
            }
            seenCommunityStrings.TryAdd(d.CommunityString, deviceName);

            var info = new DeviceInfo(deviceName, d.IpAddress, ip.ToString(), d.Port, pollGroups, d.CommunityString);
            byIpPortSeen[ipPortKey] = info;
            result.Add(info);
        }

        return result;
    }

    /// <summary>
    /// Resolves MetricNames in each poll group to OIDs via <see cref="IOidMapService.ResolveToOid"/>.
    /// Unresolvable names are logged as warnings and excluded. Resolution summary is always logged
    /// per poll group for reload diff visibility. Poll groups with zero resolved OIDs are excluded
    /// entirely (logged as Warning); devices with all-zero-OID poll groups are still registered for
    /// trap reception.
    /// </summary>
    private static ReadOnlyCollection<MetricPollInfo> BuildPollGroups(
        List<PollOptions> polls,
        string deviceName,
        IOidMapService oidMapService,
        ILogger logger)
    {
        var result = new List<MetricPollInfo>();
        var seenAggregatedNames = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < polls.Count; index++)
        {
            var poll = polls[index];
            var resolvedOids = new List<string>();
            var unresolvedNames = new List<string>();

            foreach (var name in poll.MetricNames)
            {
                var oid = oidMapService.ResolveToOid(name);
                if (oid is not null)
                    resolvedOids.Add(oid);
                else
                {
                    unresolvedNames.Add(name);
                    logger.LogWarning(
                        "MetricName '{MetricName}' on device '{DeviceName}' poll {PollIndex} not found in OID map -- skipping",
                        name, deviceName, index);
                }
            }

            logger.LogInformation(
                "Device '{DeviceName}' poll {PollIndex}: resolved {ResolvedCount}/{TotalCount} metric names{UnresolvedDetail}",
                deviceName, index, resolvedOids.Count, poll.MetricNames.Count,
                unresolvedNames.Count > 0 ? $"; unresolved: [{string.Join(", ", unresolvedNames)}]" : "");

            if (resolvedOids.Count == 0)
            {
                logger.LogWarning(
                    "Device '{DeviceName}' poll {PollIndex} has zero resolved OIDs -- skipping job registration",
                    deviceName, index);
                continue;
            }

            AggregatedMetricDefinition? aggregatedMetric = null;

            var hasName = !string.IsNullOrEmpty(poll.AggregatedMetricName);
            var hasAggregator = !string.IsNullOrEmpty(poll.Aggregator);

            if (hasName || hasAggregator)
            {
                if (!hasName || !hasAggregator)
                {
                    logger.LogError(
                        "Combined metric '{AggregatedMetricName}' on device '{DeviceName}' poll {PollGroupIndex} has {Reason} -- skipping combined metric",
                        poll.AggregatedMetricName ?? "(null)", deviceName, index,
                        !hasName ? "AggregatedMetricName missing" : "Aggregator missing");
                }
                else if (!Enum.TryParse<AggregationKind>(poll.Aggregator, ignoreCase: true, out var kind))
                {
                    logger.LogError(
                        "Combined metric '{AggregatedMetricName}' on device '{DeviceName}' poll {PollGroupIndex} has {Reason} -- skipping combined metric",
                        poll.AggregatedMetricName, deviceName, index,
                        $"invalid Aggregator '{poll.Aggregator}'");
                }
                else if (resolvedOids.Count < 2)
                {
                    logger.LogError(
                        "Combined metric '{AggregatedMetricName}' on device '{DeviceName}' poll {PollGroupIndex} has {Reason} -- skipping combined metric",
                        poll.AggregatedMetricName, deviceName, index,
                        $"fewer than 2 resolved OIDs ({resolvedOids.Count})");
                }
                else if (!seenAggregatedNames.Add(poll.AggregatedMetricName!))
                {
                    logger.LogError(
                        "Combined metric '{AggregatedMetricName}' on device '{DeviceName}' poll {PollGroupIndex} has {Reason} -- skipping combined metric",
                        poll.AggregatedMetricName, deviceName, index,
                        "duplicate AggregatedMetricName on this device");
                }
                else if (oidMapService.ContainsMetricName(poll.AggregatedMetricName!))
                {
                    logger.LogError(
                        "Combined metric '{AggregatedMetricName}' on device '{DeviceName}' poll {PollGroupIndex} has {Reason} -- skipping combined metric",
                        poll.AggregatedMetricName, deviceName, index,
                        "name collides with existing OID map entry");
                }
                else
                {
                    aggregatedMetric = new AggregatedMetricDefinition(
                        poll.AggregatedMetricName!,
                        kind,
                        resolvedOids.AsReadOnly());
                }
            }

            result.Add(new MetricPollInfo(
                PollIndex: index,
                Oids: resolvedOids.AsReadOnly(),
                IntervalSeconds: poll.IntervalSeconds,
                TimeoutMultiplier: poll.TimeoutMultiplier,
                GraceMultiplier: poll.GraceMultiplier)
            {
                AggregatedMetrics = aggregatedMetric is not null ? [aggregatedMetric] : []
            });
        }
        return result.AsReadOnly();
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
