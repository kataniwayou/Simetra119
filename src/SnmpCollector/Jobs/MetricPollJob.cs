using Lextm.SharpSnmpLib;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;
using System.Diagnostics;
using System.Net;

namespace SnmpCollector.Jobs;

/// <summary>
/// Quartz <see cref="IJob"/> that executes a single SNMP GET poll for one device/poll-group pair.
/// Each returned varbind is dispatched individually via <see cref="ISender.Send"/> into the
/// MediatR pipeline (Logging → Exception → Validation → OidResolution → OtelMetricHandler).
/// Uses per-device Port from configuration and derives CommunityString from device name at runtime.
/// <para>
/// <see cref="DisallowConcurrentExecution"/> prevents pile-up on slow devices: if a previous
/// execution is still running when the trigger fires, Quartz skips the fire.
/// </para>
/// </summary>
[DisallowConcurrentExecution]
public sealed class MetricPollJob : IJob
{
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly IDeviceUnreachabilityTracker _unreachabilityTracker;
    private readonly ISender _sender;
    private readonly ISnmpClient _snmpClient;
    private readonly ICorrelationService _correlation;
    private readonly ILivenessVectorService _liveness;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly ILogger<MetricPollJob> _logger;

    public MetricPollJob(
        IDeviceRegistry deviceRegistry,
        IDeviceUnreachabilityTracker unreachabilityTracker,
        ISender sender,
        ISnmpClient snmpClient,
        ICorrelationService correlation,
        ILivenessVectorService liveness,
        PipelineMetricService pipelineMetrics,
        ILogger<MetricPollJob> logger)
    {
        _deviceRegistry = deviceRegistry;
        _unreachabilityTracker = unreachabilityTracker;
        _sender = sender;
        _snmpClient = snmpClient;
        _correlation = correlation;
        _liveness = liveness;
        _pipelineMetrics = pipelineMetrics;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        // Capture the current global correlationId at job start so all logs during this
        // execution carry a consistent ID even if the global one rotates mid-execution.
        _correlation.OperationCorrelationId = _correlation.CurrentCorrelationId;

        var map = context.MergedJobDataMap;
        var configAddress = map.GetString("configAddress")!;
        var port = map.GetInt("port");
        var pollIndex = map.GetInt("pollIndex");
        var intervalSeconds = map.GetInt("intervalSeconds");
        var jobKey = context.JobDetail.Key.Name;

        // Device lookup by config address (DNS or IP as configured).
        // Config errors (device removed after scheduler started) do NOT count as a poll execution.
        if (!_deviceRegistry.TryGetByIpPort(configAddress, port, out var device))
        {
            _logger.LogWarning(
                "Poll job {JobKey}: device at {ConfigAddress}:{Port} not found in registry -- skipping poll",
                jobKey, configAddress, port);
            return;
        }

        var pollGroup = device.PollGroups[pollIndex];

        // Build variable list from poll group OIDs only (no sysUpTime prepend).
        var variables = pollGroup.Oids
            .Select(oid => new Variable(new ObjectIdentifier(oid)))
            .ToList();

        // Use resolved IP for actual SNMP GET call
        var endpoint = new IPEndPoint(IPAddress.Parse(device.ResolvedIp), device.Port);
        var communityStr = !string.IsNullOrEmpty(device.CommunityString)
            ? device.CommunityString
            : CommunityStringHelper.DeriveFromDeviceName(device.Name);
        var community = new OctetString(communityStr);

        try
        {
            // Timeout as configured multiplier of interval — leaves headroom before next trigger.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(intervalSeconds * pollGroup.TimeoutMultiplier));

            var sw = Stopwatch.StartNew();
            var response = await _snmpClient.GetAsync(
                VersionCode.V2,
                endpoint,
                community,
                variables,
                timeoutCts.Token);
            sw.Stop();

            await DispatchResponseAsync(response, device, sw.Elapsed.TotalMilliseconds, context.CancellationToken);

            // Success: reset failure counter; log + counter only on recovered transition.
            if (_unreachabilityTracker.RecordSuccess(device.Name))
            {
                _logger.LogInformation(
                    "Device {Name} ({Ip}) recovered after consecutive failures",
                    device.Name, device.ResolvedIp);
                _pipelineMetrics.IncrementPollRecovered(device.Name);
            }
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            // Timeout: the linked CTS fired, but host is not shutting down.
            _logger.LogWarning(
                "Poll job {JobKey} timed out waiting for SNMP response from {DeviceName} ({Ip})",
                jobKey, device.Name, device.ResolvedIp);
            RecordFailure(device.Name, device);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown: context.CancellationToken was cancelled.
            // Re-throw so Quartz handles graceful shutdown correctly.
            throw;
        }
        catch (Exception ex)
        {
            // Network error, SNMP error, or any other unexpected failure.
            _logger.LogWarning(ex,
                "Poll job {JobKey} failed for {DeviceName} ({Ip})",
                jobKey, device.Name, device.ResolvedIp);
            RecordFailure(device.Name, device);
        }
        finally
        {
            // SC#4: always increment after every completed poll attempt, success or failure.
            _pipelineMetrics.IncrementPollExecuted(device.Name);
            // HLTH-05: Stamp liveness vector on completion (always, even on failure)
            _liveness.Stamp(jobKey);
            // Clear operation-scoped correlationId so it doesn't leak to other async contexts.
            _correlation.OperationCorrelationId = null;
        }
    }

    /// <summary>
    /// Dispatches each varbind from the SNMP GET response individually via ISender.Send.
    /// Skips noSuchObject / noSuchInstance / EndOfMibView varbinds with a Debug log.
    /// </summary>
    private async Task DispatchResponseAsync(
        IList<Variable> response,
        DeviceInfo device,
        double pollDurationMs,
        CancellationToken ct)
    {
        foreach (var variable in response)
        {
            // Skip error sentinels — device doesn't expose this OID.
            if (variable.Data.TypeCode is SnmpType.NoSuchObject
                                       or SnmpType.NoSuchInstance
                                       or SnmpType.EndOfMibView)
            {
                _logger.LogDebug(
                    "OID {Oid} returned {TypeCode} from {DeviceName} -- skipping",
                    variable.Id, variable.Data.TypeCode, device.Name);
                continue;
            }

            var msg = new SnmpOidReceived
            {
                Oid = variable.Id.ToString(),
                AgentIp = IPAddress.Parse(device.ResolvedIp),
                DeviceName = device.Name,
                Value = variable.Data,
                Source = SnmpSource.Poll,
                TypeCode = variable.Data.TypeCode,
                PollDurationMs = pollDurationMs
            };

            await _sender.Send(msg, ct);
        }
    }

    /// <summary>
    /// Records a poll failure and fires the unreachability transition counter + log on state change.
    /// </summary>
    private void RecordFailure(string deviceName, DeviceInfo device)
    {
        if (_unreachabilityTracker.RecordFailure(deviceName))
        {
            var failureCount = _unreachabilityTracker.GetFailureCount(deviceName);
            _logger.LogWarning(
                "Device {Name} ({Ip}) unreachable after {N} consecutive failures",
                device.Name, device.ResolvedIp, failureCount);
            _pipelineMetrics.IncrementPollUnreachable(deviceName);
        }
    }
}
