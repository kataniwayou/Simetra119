using System.Text.Json;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SnmpCollector.Pipeline;

namespace SnmpCollector.Services;

/// <summary>
/// Background service that watches the <c>simetra-oid-command-map</c> ConfigMap via the Kubernetes
/// API and triggers a command map reload on change. Only updates CommandMapService -- does not
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
public sealed class CommandMapWatcherService : BackgroundService
{
    /// <summary>
    /// ConfigMap name containing the OID-to-command-name mapping.
    /// </summary>
    internal const string ConfigMapName = "simetra-oid-command-map";

    /// <summary>
    /// Key within the ConfigMap data that holds the command map JSON document.
    /// </summary>
    internal const string ConfigKey = "oid_command_map.json";

    private readonly IKubernetes _kubeClient;
    private readonly ICommandMapService _commandMapService;
    private readonly ILogger<CommandMapWatcherService> _logger;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private readonly string _namespace;

    public CommandMapWatcherService(
        IKubernetes kubeClient,
        ICommandMapService commandMapService,
        ILogger<CommandMapWatcherService> logger)
    {
        _kubeClient = kubeClient ?? throw new ArgumentNullException(nameof(kubeClient));
        _commandMapService = commandMapService ?? throw new ArgumentNullException(nameof(commandMapService));
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
                "CommandMapWatcher initial load complete for {ConfigMap}/{Key} in namespace {Namespace}",
                ConfigMapName, ConfigKey, _namespace);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "CommandMapWatcher initial load failed for {ConfigMap}/{Key} -- will retry via watch loop",
                ConfigMapName, ConfigKey);
        }

        // Watch loop with automatic reconnect (K8s watch timeout ~30 min)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug(
                    "CommandMapWatcher starting watch on {ConfigMap} in namespace {Namespace}",
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
                            "CommandMapWatcher received {EventType} event for {ConfigMap}",
                            eventType, ConfigMapName);

                        await HandleConfigMapChangedAsync(configMap, stoppingToken).ConfigureAwait(false);
                    }
                    else if (eventType is WatchEventType.Deleted)
                    {
                        _logger.LogWarning(
                            "ConfigMap {ConfigMap} was deleted -- skipping reload, retaining current command map",
                            ConfigMapName);
                    }
                }

                // Watch ended normally (server closed the connection after ~30 min)
                _logger.LogDebug("CommandMapWatcher watch connection closed, reconnecting");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown -- exit the loop
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "CommandMapWatcher watch disconnected unexpectedly, reconnecting in 5s");

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

        _logger.LogInformation("CommandMapWatcher stopped");
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
    /// Parses the command map JSON from the ConfigMap and applies the new mapping
    /// to <see cref="ICommandMapService"/>.
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

        var commandMap = ValidateAndParseCommandMap(jsonContent, _logger);
        if (commandMap is null)
            return;

        await _reloadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _commandMapService.UpdateMap(commandMap);

            _logger.LogInformation(
                "Command map reload complete: {CommandCount} entries",
                commandMap.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command map reload failed -- previous map remains active");
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    /// <summary>
    /// Parses command map JSON using <see cref="JsonDocument"/> to detect duplicate OID keys
    /// and duplicate command name values. Both occurrences of any duplicate are skipped
    /// (neither enters the returned dictionary). Returns <c>null</c> only on JSON parse failure.
    /// </summary>
    /// <param name="jsonContent">Raw JSON string from the ConfigMap.</param>
    /// <param name="logger">Logger for structured warnings/errors.</param>
    /// <returns>
    /// Clean dictionary of OID-to-command-name entries with all duplicates removed,
    /// or <c>null</c> if the JSON is malformed.
    /// </returns>
    internal static Dictionary<string, string>? ValidateAndParseCommandMap(string jsonContent, ILogger logger)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonContent, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (JsonException ex)
        {
            logger.LogError(ex,
                "Failed to parse {ConfigKey} from ConfigMap {ConfigMap} -- skipping reload",
                ConfigKey, ConfigMapName);
            return null;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                logger.LogError(
                    "CommandMap JSON must be an array of objects -- skipping reload");
                return null;
            }

            // Pass 1: Enumerate all array elements, detect duplicate OID keys
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicateOids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rawEntries = new List<(string oid, string name)>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("Oid", out var oidProp) ||
                    !element.TryGetProperty("CommandName", out var nameProp))
                {
                    logger.LogWarning(
                        "CommandMap array element missing Oid or CommandName -- skipping entry");
                    continue;
                }

                var oid = oidProp.GetString();
                var name = nameProp.ValueKind == JsonValueKind.Null
                    ? null
                    : nameProp.GetString();

                if (string.IsNullOrEmpty(oid))
                {
                    logger.LogWarning(
                        "CommandMap array element has null or empty Oid -- skipping entry");
                    continue;
                }

                if (string.IsNullOrEmpty(name))
                {
                    logger.LogWarning(
                        "CommandMap entry skipped: {Oid} has null or empty command name",
                        oid);
                    continue;
                }

                if (!seen.Add(oid))
                {
                    duplicateOids.Add(oid);
                }
                else
                {
                    rawEntries.Add((oid, name));
                }
            }

            foreach (var oid in duplicateOids)
            {
                logger.LogWarning(
                    "CommandMap duplicate OID key detected: {Oid} appears multiple times -- all entries for this OID will be skipped",
                    oid);
            }

            // Pass 2: Detect duplicate command name values
            var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (_, name) in rawEntries)
            {
                nameCounts.TryGetValue(name, out var count);
                nameCounts[name] = count + 1;
            }

            var duplicateNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (name, count) in nameCounts)
            {
                if (count > 1)
                    duplicateNames.Add(name);
            }

            foreach (var name in duplicateNames)
            {
                logger.LogWarning(
                    "CommandMap duplicate command name detected: {CommandName} maps to multiple OIDs -- all entries for this name will be skipped",
                    name);
            }

            // Pass 3: Build clean dictionary, filtering out both categories
            var cleanEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (oid, name) in rawEntries)
            {
                if (duplicateOids.Contains(oid))
                {
                    logger.LogWarning(
                        "CommandMap duplicate OID key skipped: {Oid} -> {CommandName}",
                        oid, name);
                    continue;
                }

                if (duplicateNames.Contains(name))
                {
                    logger.LogWarning(
                        "CommandMap duplicate command name skipped: {Oid} -> {CommandName}",
                        oid, name);
                    continue;
                }

                cleanEntries[oid] = name;
            }

            if (cleanEntries.Count == 0 && rawEntries.Count > 0)
            {
                logger.LogWarning(
                    "CommandMap validation produced empty map -- all entries were duplicates or invalid");
            }

            return cleanEntries;
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
