using Lextm.SharpSnmpLib;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;
using System.Diagnostics;
using System.Net;

namespace SnmpCollector.Services;

/// <summary>
/// BackgroundService that drains <see cref="CommandRequest"/> items from the command channel
/// and executes SNMP SET operations. Each SET response varbind is dispatched as an
/// <see cref="SnmpOidReceived"/> request through the MediatR pipeline via <see cref="ISender.Send"/>
/// with <see cref="SnmpSource.Command"/>.
///
/// Mirrors <see cref="ChannelConsumerService"/> for drain loop structure: same await foreach,
/// same per-item try/catch, same correlation ID wiring.
///
/// SET timeout uses <see cref="SnapshotJobOptions.IntervalSeconds"/> * <see cref="SnapshotJobOptions.TimeoutMultiplier"/>
/// (same pattern as <see cref="Jobs.MetricPollJob"/>).
/// </summary>
public sealed class CommandWorkerService : BackgroundService
{
    private readonly ICommandChannel _commandChannel;
    private readonly ISnmpClient _snmpClient;
    private readonly ISender _sender;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly ICommandMapService _commandMapService;
    private readonly ICorrelationService _correlation;
    private readonly ILeaderElection _leaderElection;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly IOptions<SnapshotJobOptions> _snapshotJobOptions;
    private readonly ILogger<CommandWorkerService> _logger;

    public CommandWorkerService(
        ICommandChannel commandChannel,
        ISnmpClient snmpClient,
        ISender sender,
        IDeviceRegistry deviceRegistry,
        ICommandMapService commandMapService,
        ICorrelationService correlation,
        ILeaderElection leaderElection,
        PipelineMetricService pipelineMetrics,
        IOptions<SnapshotJobOptions> snapshotJobOptions,
        ILogger<CommandWorkerService> logger)
    {
        _commandChannel = commandChannel;
        _snmpClient = snmpClient;
        _sender = sender;
        _deviceRegistry = deviceRegistry;
        _commandMapService = commandMapService;
        _correlation = correlation;
        _leaderElection = leaderElection;
        _pipelineMetrics = pipelineMetrics;
        _snapshotJobOptions = snapshotJobOptions;
        _logger = logger;
    }

    /// <summary>
    /// Single consumer loop that reads <see cref="CommandRequest"/> items from the command channel,
    /// executes SNMP SET operations, and dispatches response varbinds through the MediatR pipeline.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Command channel worker started");

        await foreach (var req in _commandChannel.Reader.ReadAllAsync(stoppingToken))
        {
            _correlation.OperationCorrelationId = _correlation.CurrentCorrelationId;
            try
            {
                await ExecuteCommandAsync(req, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Command {CommandName} for {Target} failed",
                    req.CommandName, $"{req.Ip}:{req.Port}");
                _pipelineMetrics.IncrementCommandFailed($"{req.Ip}:{req.Port}");
            }
            finally
            {
                _correlation.OperationCorrelationId = null;
            }
        }

        _logger.LogInformation("Command channel worker completed");
    }

    private async Task ExecuteCommandAsync(CommandRequest req, CancellationToken stoppingToken)
    {
        // 1. Resolve OID from command name
        var oid = _commandMapService.ResolveCommandOid(req.CommandName);
        if (oid is null)
        {
            _logger.LogWarning(
                "Command {CommandName} not found in command map for {Target} -- skipping",
                req.CommandName, $"{req.Ip}:{req.Port}");
            _pipelineMetrics.IncrementCommandFailed($"{req.Ip}:{req.Port}");
            return;
        }

        // 2. Resolve device for community string
        if (!_deviceRegistry.TryGetByIpPort(req.Ip, req.Port, out var device))
        {
            _logger.LogWarning(
                "Device {Ip}:{Port} not found in registry -- skipping",
                req.Ip, req.Port);
            _pipelineMetrics.IncrementCommandFailed($"{req.Ip}:{req.Port}");
            return;
        }

        // 3. Build Variable using SharpSnmpClient.ParseSnmpData
        var snmpData = SharpSnmpClient.ParseSnmpData(req.Value, req.ValueType);
        var variable = new Variable(new ObjectIdentifier(oid), snmpData);

        // 4. SET with timeout (mirrors MetricPollJob lines 92-93)
        var intervalSeconds = _snapshotJobOptions.Value.IntervalSeconds;
        var timeoutMultiplier = _snapshotJobOptions.Value.TimeoutMultiplier;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(intervalSeconds * timeoutMultiplier));

        var endpoint = new IPEndPoint(IPAddress.Parse(device.ResolvedIp), device.Port);
        var community = new OctetString(device.CommunityString);

        // 5. Leader gate — only the leader sends SET commands to devices.
        // Checked as close to SetAsync as possible so leadership changes mid-cycle are respected.
        if (!_leaderElection.IsLeader)
        {
            _logger.LogDebug(
                "Skipping SET {CommandName} for {DeviceName} — not leader",
                req.CommandName, device.Name);
            return;
        }

        IList<Variable> response;
        var sw = Stopwatch.StartNew();
        try
        {
            response = await _snmpClient.SetAsync(
                VersionCode.V2, endpoint, community, variable, timeoutCts.Token);
            sw.Stop();
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // Timeout: the linked CTS fired, but host is not shutting down.
            sw.Stop();
            _logger.LogWarning(
                "Command {CommandName} timed out for {DeviceName} after {DurationMs:F1}ms",
                req.CommandName, device.Name, sw.Elapsed.TotalMilliseconds);
            _pipelineMetrics.IncrementCommandFailed(device.Name);
            return;
        }

        // 5. Dispatch response varbinds through full MediatR pipeline
        foreach (var varbind in response)
        {
            var metricName = _commandMapService.ResolveCommandName(varbind.Id.ToString());

            var msg = new SnmpOidReceived
            {
                Oid        = varbind.Id.ToString(),
                AgentIp    = IPAddress.Parse(device.ResolvedIp),
                DeviceName = device.Name,
                Value      = varbind.Data,
                Source     = SnmpSource.Command,
                TypeCode   = varbind.Data.TypeCode,
                MetricName = metricName,               // pre-set if found; null triggers OidResolution fallback
            };

            await _sender.Send(msg, stoppingToken);
        }

        // 6. Increment success counter after all varbinds dispatched
        _pipelineMetrics.IncrementCommandSent(device.Name);

        _logger.LogInformation(
            "Command {CommandName} completed for {DeviceName} in {DurationMs:F1}ms",
            req.CommandName, device.Name, sw.Elapsed.TotalMilliseconds);
    }
}
