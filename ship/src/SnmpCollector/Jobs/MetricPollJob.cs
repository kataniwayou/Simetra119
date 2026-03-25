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
/// Uses per-device Port and CommunityString directly from configuration.
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
        var communityStr = device.CommunityString;
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

            await DispatchResponseAsync(response, device, pollGroup, sw.Elapsed.TotalMilliseconds, context.CancellationToken);

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
    /// Dispatches each varbind from the SNMP GET response individually via ISender.Send,
    /// then computes and dispatches any configured aggregate metrics.
    /// Skips noSuchObject / noSuchInstance / EndOfMibView varbinds with a Debug log.
    /// </summary>
    private async Task DispatchResponseAsync(
        IList<Variable> response,
        DeviceInfo device,
        MetricPollInfo pollGroup,
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

        // CM-08/CM-10: compute and dispatch aggregated metrics after all individual varbinds
        foreach (var aggregated in pollGroup.AggregatedMetrics)
        {
            try
            {
                await DispatchAggregatedMetricAsync(aggregated, response, device, ct);
            }
            catch (Exception ex)
            {
                // CM decision: aggregate exceptions do NOT call RecordFailure / increment snmp_poll_unreachable_total
                _logger.LogError(ex,
                    "Aggregated metric {MetricName} dispatch failed for {DeviceName} poll group {PollIndex}",
                    aggregated.MetricName, device.Name, pollGroup.PollIndex);
            }
        }
    }

    /// <summary>
    /// CM-07/CM-08/CM-09: Computes one aggregate metric from the SNMP response and dispatches
    /// it as a synthetic SnmpOidReceived through the full MediatR pipeline.
    /// </summary>
    private async Task DispatchAggregatedMetricAsync(
        AggregatedMetricDefinition aggregated,
        IList<Variable> response,
        DeviceInfo device,
        CancellationToken ct)
    {
        // Build OID→data lookup from response (excluding error sentinels)
        var oidValues = new Dictionary<string, ISnmpData>(response.Count);
        foreach (var v in response)
        {
            if (v.Data.TypeCode is not SnmpType.NoSuchObject
                                and not SnmpType.NoSuchInstance
                                and not SnmpType.EndOfMibView)
            {
                oidValues[v.Id.ToString()] = v.Data;
            }
        }

        // CM-09: all source OIDs must be present and numeric
        var values = new List<double>(aggregated.SourceOids.Count);
        foreach (var oid in aggregated.SourceOids)
        {
            if (!oidValues.TryGetValue(oid, out var data))
            {
                _logger.LogWarning(
                    "Aggregated metric {MetricName} skipped: OID {Oid} absent from response for {DeviceName}",
                    aggregated.MetricName, oid, device.Name);
                return;
            }
            if (!IsNumeric(data.TypeCode))
            {
                _logger.LogWarning(
                    "Aggregated metric {MetricName} skipped: OID {Oid} is non-numeric ({TypeCode}) for {DeviceName}",
                    aggregated.MetricName, oid, data.TypeCode, device.Name);
                return;
            }
            values.Add(ExtractNumericValue(data));
        }

        // CM-08: compute aggregate
        var result = Compute(aggregated.Kind, values);
        var typeCode = SelectTypeCode(aggregated.Kind);

        // Construct ISnmpData Value wrapper with clamping for overflow safety
        ISnmpData value = typeCode == SnmpType.Integer32
            ? new Integer32((int)Math.Clamp(result, int.MinValue, int.MaxValue))
            : new Gauge32((uint)Math.Clamp(result, uint.MinValue, uint.MaxValue));

        // CM-07, CM-10: dispatch through full MediatR pipeline
        var syntheticMsg = new SnmpOidReceived
        {
            Oid        = "0.0",                              // sentinel OID (passes ValidationBehavior regex)
            AgentIp    = IPAddress.Parse(device.ResolvedIp),
            DeviceName = device.Name,                        // REQUIRED: ValidationBehavior rejects null DeviceName
            Value      = value,
            Source     = SnmpSource.Synthetic,               // causes OidResolutionBehavior to bypass
            TypeCode   = typeCode,
            MetricName = aggregated.MetricName,              // pre-set: preserved through OidResolution bypass
            PollDurationMs = null                            // no round-trip for synthetic metrics
        };

        await _sender.Send(syntheticMsg, ct);
    }

    private static bool IsNumeric(SnmpType typeCode) => typeCode is
        SnmpType.Integer32 or
        SnmpType.Gauge32 or
        SnmpType.TimeTicks or
        SnmpType.Counter32 or
        SnmpType.Counter64;

    private static double ExtractNumericValue(ISnmpData data) => data.TypeCode switch
    {
        SnmpType.Integer32  => ((Integer32)data).ToInt32(),
        SnmpType.Gauge32    => ((Gauge32)data).ToUInt32(),
        SnmpType.TimeTicks  => ((TimeTicks)data).ToUInt32(),
        SnmpType.Counter32  => ((Counter32)data).ToUInt32(),
        SnmpType.Counter64  => (double)((Counter64)data).ToUInt64(),
        _                   => throw new InvalidOperationException($"Non-numeric TypeCode {data.TypeCode}")
    };

    private static double Compute(AggregationKind kind, IReadOnlyList<double> values) => kind switch
    {
        AggregationKind.Sum      => values.Sum(),
        AggregationKind.Subtract => values.Skip(1).Aggregate(values[0], (acc, v) => acc - v),
        AggregationKind.AbsDiff  => Math.Abs(values.Skip(1).Aggregate(values[0], (acc, v) => acc - v)),
        AggregationKind.Mean     => values.Sum() / values.Count,
        _                        => throw new InvalidOperationException($"Unknown AggregationKind {kind}")
    };

    private static SnmpType SelectTypeCode(AggregationKind kind) => kind switch
    {
        AggregationKind.Subtract or AggregationKind.AbsDiff => SnmpType.Integer32,
        AggregationKind.Sum      or AggregationKind.Mean    => SnmpType.Gauge32,
        _                                                    => SnmpType.Gauge32
    };

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
