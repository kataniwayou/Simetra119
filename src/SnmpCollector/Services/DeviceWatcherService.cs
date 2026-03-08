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
    private readonly ILogger<DeviceWatcherService> _logger;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private readonly string _namespace;

    public DeviceWatcherService(
        IKubernetes kubeClient,
        IDeviceRegistry deviceRegistry,
        DynamicPollScheduler pollScheduler,
        ILogger<DeviceWatcherService> logger)
    {
        _kubeClient = kubeClient ?? throw new ArgumentNullException(nameof(kubeClient));
        _deviceRegistry = deviceRegistry ?? throw new ArgumentNullException(nameof(deviceRegistry));
        _pollScheduler = pollScheduler ?? throw new ArgumentNullException(nameof(pollScheduler));
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
    /// Parses the devices JSON array from the ConfigMap and applies the new device list
    /// to <see cref="IDeviceRegistry"/> and <see cref="DynamicPollScheduler"/>.
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
            // 1. Reload device registry (async DNS resolution)
            await _deviceRegistry.ReloadAsync(devices).ConfigureAwait(false);

            // 2. Reconcile Quartz poll jobs to match new device config
            await _pollScheduler.ReconcileAsync(devices, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Device reload complete: {DeviceCount} devices",
                devices.Count);
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
