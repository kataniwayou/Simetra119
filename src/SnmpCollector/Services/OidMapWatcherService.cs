using System.Text.Json;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SnmpCollector.Pipeline;

namespace SnmpCollector.Services;

/// <summary>
/// Background service that watches the <c>simetra-oidmaps</c> ConfigMap via the Kubernetes
/// API and triggers an OID map reload on change. Only updates OidMapService -- does not
/// touch DeviceRegistry or DynamicPollScheduler.
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
public sealed class OidMapWatcherService : BackgroundService
{
    /// <summary>
    /// ConfigMap name containing the OID-to-metric-name mapping.
    /// </summary>
    internal const string ConfigMapName = "simetra-oidmaps";

    /// <summary>
    /// Key within the ConfigMap data that holds the OID map JSON document.
    /// </summary>
    internal const string ConfigKey = "oidmaps.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IKubernetes _kubeClient;
    private readonly IOidMapService _oidMapService;
    private readonly ILogger<OidMapWatcherService> _logger;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private readonly string _namespace;

    public OidMapWatcherService(
        IKubernetes kubeClient,
        IOidMapService oidMapService,
        ILogger<OidMapWatcherService> logger)
    {
        _kubeClient = kubeClient ?? throw new ArgumentNullException(nameof(kubeClient));
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
                "OidMapWatcher initial load complete for {ConfigMap}/{Key} in namespace {Namespace}",
                ConfigMapName, ConfigKey, _namespace);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "OidMapWatcher initial load failed for {ConfigMap}/{Key} -- will retry via watch loop",
                ConfigMapName, ConfigKey);
        }

        // Watch loop with automatic reconnect (K8s watch timeout ~30 min)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug(
                    "OidMapWatcher starting watch on {ConfigMap} in namespace {Namespace}",
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
                            "OidMapWatcher received {EventType} event for {ConfigMap}",
                            eventType, ConfigMapName);

                        await HandleConfigMapChangedAsync(configMap, stoppingToken).ConfigureAwait(false);
                    }
                    else if (eventType is WatchEventType.Deleted)
                    {
                        _logger.LogWarning(
                            "ConfigMap {ConfigMap} was deleted -- skipping reload, retaining current OID map",
                            ConfigMapName);
                    }
                }

                // Watch ended normally (server closed the connection after ~30 min)
                _logger.LogDebug("OidMapWatcher watch connection closed, reconnecting");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown -- exit the loop
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "OidMapWatcher watch disconnected unexpectedly, reconnecting in 5s");

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

        _logger.LogInformation("OidMapWatcher stopped");
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
    /// Parses the OID map JSON from the ConfigMap and applies the new mapping
    /// to <see cref="IOidMapService"/>.
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

        Dictionary<string, string>? oidMap;
        try
        {
            oidMap = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "Failed to parse {ConfigKey} from ConfigMap {ConfigMap} -- skipping reload",
                ConfigKey, ConfigMapName);
            return;
        }

        if (oidMap is null)
        {
            _logger.LogWarning(
                "Deserialized {ConfigKey} is null -- skipping reload",
                ConfigKey);
            return;
        }

        await _reloadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _oidMapService.UpdateMap(oidMap);

            _logger.LogInformation(
                "OID map reload complete: {OidCount} entries",
                oidMap.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OID map reload failed -- previous map remains active");
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
