using Lextm.SharpSnmpLib;
using MediatR;
using Microsoft.Extensions.Logging;
using Quartz;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;
using System.Net;

namespace SnmpCollector.Jobs;

/// <summary>
/// Quartz <see cref="IJob"/> that executes a single SNMP GET poll for one device/poll-group pair.
/// Prepends sysUpTime to every GET so the counter delta engine has uptime context atomically.
/// Each returned varbind is dispatched individually via <see cref="ISender.Send"/> into the
/// MediatR pipeline (Logging → Exception → Validation → OidResolution → OtelMetricHandler).
/// <para>
/// <see cref="DisallowConcurrentExecution"/> prevents pile-up on slow devices: if a previous
/// execution is still running when the trigger fires, Quartz skips the fire.
/// </para>
/// </summary>
[DisallowConcurrentExecution]
public sealed class MetricPollJob : IJob
{
    private const string SysUpTimeOid = "1.3.6.1.2.1.1.3.0";

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
        var deviceName = map.GetString("deviceName")!;
        var pollIndex = map.GetInt("pollIndex");
        var intervalSeconds = map.GetInt("intervalSeconds");
        var jobKey = context.JobDetail.Key.Name;

        // Device lookup must succeed before we enter the try block.
        // Config errors (device removed after scheduler started) do NOT count as a poll execution.
        if (!_deviceRegistry.TryGetDeviceByName(deviceName, out var device))
        {
            _logger.LogWarning(
                "Poll job {JobKey}: device '{DeviceName}' not found in registry -- skipping poll",
                jobKey, deviceName);
            return;
        }

        var pollGroup = device.PollGroups[pollIndex];

        // Build variable list: sysUpTime first for atomic uptime snapshot, then poll group OIDs.
        var variables = new List<Variable>(pollGroup.Oids.Count + 1)
        {
            new Variable(new ObjectIdentifier(SysUpTimeOid))
        };
        variables.AddRange(pollGroup.Oids.Select(oid => new Variable(new ObjectIdentifier(oid))));

        var endpoint = new IPEndPoint(IPAddress.Parse(device.IpAddress), 161);
        var community = new OctetString(device.CommunityString);

        try
        {
            // 80% of the interval as timeout (SC#2) — leaves response window before next trigger.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(intervalSeconds * 0.8));

            var response = await _snmpClient.GetAsync(
                VersionCode.V2,
                endpoint,
                community,
                variables,
                timeoutCts.Token);

            await DispatchResponseAsync(response, device, context.CancellationToken);

            // Success: reset failure counter; log + counter only on recovered transition.
            if (_unreachabilityTracker.RecordSuccess(deviceName))
            {
                _logger.LogInformation(
                    "Device {Name} ({Ip}) recovered after consecutive failures",
                    device.Name, device.IpAddress);
                _pipelineMetrics.IncrementPollRecovered();
            }
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            // Timeout: the linked CTS fired, but host is not shutting down.
            _logger.LogWarning(
                "Poll job {JobKey} timed out waiting for SNMP response from {DeviceName} ({Ip})",
                jobKey, device.Name, device.IpAddress);
            RecordFailure(deviceName, device);
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
                jobKey, device.Name, device.IpAddress);
            RecordFailure(deviceName, device);
        }
        finally
        {
            // SC#4: always increment after every completed poll attempt, success or failure.
            _pipelineMetrics.IncrementPollExecuted();
            // HLTH-05: Stamp liveness vector on completion (always, even on failure)
            _liveness.Stamp(jobKey);
            // Clear operation-scoped correlationId so it doesn't leak to other async contexts.
            _correlation.OperationCorrelationId = null;
        }
    }

    /// <summary>
    /// Dispatches each varbind from the SNMP GET response individually via ISender.Send.
    /// Extracts sysUpTime from the first TimeTicks variable for attachment to subsequent OID messages.
    /// Skips noSuchObject / noSuchInstance / EndOfMibView varbinds with a Debug log.
    /// </summary>
    private async Task DispatchResponseAsync(
        IList<Variable> response,
        DeviceInfo device,
        CancellationToken ct)
    {
        uint? sysUpTime = null;

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
                AgentIp = IPAddress.Parse(device.IpAddress),
                DeviceName = device.Name,
                Value = variable.Data,
                Source = SnmpSource.Poll,
                TypeCode = variable.Data.TypeCode,
                // sysUpTime is null for the sysUpTime varbind itself (not yet extracted at dispatch time).
                // Subsequent OIDs carry the extracted value.
                SysUpTimeCentiseconds = sysUpTime
            };

            await _sender.Send(msg, ct);

            // Extract sysUpTime AFTER dispatching the sysUpTime varbind itself so that
            // the sysUpTime message carries SysUpTimeCentiseconds = null, while all
            // subsequent OIDs carry the extracted value.
            if (variable.Id.ToString() == SysUpTimeOid
                && variable.Data.TypeCode == SnmpType.TimeTicks)
            {
                sysUpTime = ((TimeTicks)variable.Data).ToUInt32();
            }
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
                device.Name, device.IpAddress, failureCount);
            _pipelineMetrics.IncrementPollUnreachable();
        }
    }
}
