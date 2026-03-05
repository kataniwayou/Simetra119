using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;

namespace SnmpCollector.Services;

/// <summary>
/// BackgroundService that binds a UDP socket on the configured address and port to receive
/// SNMPv2c traps. Each received datagram is authenticated via community string against the
/// device registry. Authenticated varbinds are written as VarbindEnvelopes to the per-device
/// BoundedChannel for consumption by ChannelConsumerService.
///
/// ARCHITECTURAL CONSTRAINT: This class NEVER references ISender, IPublisher, IMediator, or
/// MediatR. All data flows through per-device channels via IDeviceChannelManager only.
/// </summary>
public sealed class SnmpTrapListenerService : BackgroundService
{
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly IDeviceChannelManager _channelManager;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly SnmpListenerOptions _listenerOptions;
    private readonly ILogger<SnmpTrapListenerService> _logger;

    /// <summary>
    /// Tracks which device names have received their first trap since startup.
    /// ConcurrentDictionary.TryAdd is the atomic "log once" mechanism.
    /// Value (byte) is unused; chosen for minimum memory footprint.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _seenDevices = new();

    /// <summary>
    /// SharpSnmpLib requires a UserRegistry even for SNMPv2c (no USM users needed).
    /// Creating it once avoids repeated allocation per datagram.
    /// </summary>
    private readonly UserRegistry _userRegistry = new();

    public SnmpTrapListenerService(
        IDeviceRegistry deviceRegistry,
        IDeviceChannelManager channelManager,
        PipelineMetricService pipelineMetrics,
        IOptions<SnmpListenerOptions> listenerOptions,
        ILogger<SnmpTrapListenerService> logger)
    {
        _deviceRegistry = deviceRegistry;
        _channelManager = channelManager;
        _pipelineMetrics = pipelineMetrics;
        _listenerOptions = listenerOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Binds the UDP socket and enters the receive loop until cancellation is requested.
    /// Each datagram is processed synchronously via ProcessDatagram — TryWrite is non-blocking.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bindAddress = IPAddress.Parse(_listenerOptions.BindAddress);
        var endpoint = new IPEndPoint(bindAddress, _listenerOptions.Port);

        using var udpClient = new UdpClient(endpoint);

        _logger.LogInformation(
            "Trap listener bound to UDP {Port}, monitoring {DeviceCount} devices",
            _listenerOptions.Port,
            _deviceRegistry.AllDevices.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await udpClient.ReceiveAsync(stoppingToken);
                ProcessDatagram(result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException ex)
            {
                _logger.LogWarning("Socket error receiving SNMP trap: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Malformed SNMP packet from unknown source: {Message}", ex.Message);
            }
        }
    }

    /// <summary>
    /// Overrides StopAsync to signal all channel consumers to drain after the receive loop exits.
    /// Order: base.StopAsync cancels ExecuteAsync first, then CompleteAll signals consumers.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        _channelManager.CompleteAll();
    }

    /// <summary>
    /// Parses a UDP datagram, authenticates the sender, and routes each varbind to the
    /// device's channel. Invalid packets and unauthorized senders are dropped with telemetry.
    /// </summary>
    private void ProcessDatagram(UdpReceiveResult result)
    {
        IList<ISnmpMessage> messages;
        try
        {
            messages = MessageFactory.ParseMessages(result.Buffer, 0, result.Buffer.Length, _userRegistry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Malformed SNMP packet from {SourceIp}: {Error}",
                result.RemoteEndPoint.Address,
                ex.Message);
            return;
        }

        foreach (var message in messages)
        {
            if (message is not TrapV2Message trapV2)
                continue;

            // Normalize IPv6-mapped IPv4 addresses (e.g., ::ffff:192.168.1.1 → 192.168.1.1)
            var senderIp = result.RemoteEndPoint.Address.MapToIPv4();
            var receivedCommunity = trapV2.Community().ToString();
            var variables = trapV2.Variables();

            // Step 1 — Device lookup (must come first because DeviceInfo holds the expected community string)
            if (!_deviceRegistry.TryGetDevice(senderIp, out var device))
            {
                var firstOid = variables.Count > 0 ? variables[0].Id.ToString() : "(no varbinds)";
                _logger.LogWarning(
                    "Trap from unknown device {SourceIp}, first OID: {Oid}",
                    senderIp,
                    firstOid);
                _pipelineMetrics.IncrementTrapUnknownDevice();
                continue;
            }

            // Step 2 — Community string authentication (case-sensitive, RFC-compliant)
            if (!string.Equals(receivedCommunity, device.CommunityString, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Auth failure from {SourceIp} ({DeviceName}): received community '{ReceivedCommunity}'",
                    senderIp,
                    device.Name,
                    receivedCommunity);
                _pipelineMetrics.IncrementTrapAuthFailed();
                continue;
            }

            // Step 3 — First-contact logging (once per device since startup)
            if (_seenDevices.TryAdd(device.Name, 0))
            {
                _logger.LogInformation(
                    "First trap received from {DeviceName} ({Ip})",
                    device.Name,
                    senderIp);
            }

            // Step 4 — Route each varbind to the device's BoundedChannel
            var writer = _channelManager.GetWriter(device.Name);
            foreach (var variable in variables)
            {
                var envelope = new VarbindEnvelope(
                    Oid: variable.Id.ToString(),
                    Value: variable.Data,
                    TypeCode: variable.Data.TypeCode,
                    AgentIp: senderIp,
                    DeviceName: device.Name);

                writer.TryWrite(envelope);
                // Drop telemetry is handled by the itemDropped callback in DeviceChannelManager
                // (increments snmp.trap.dropped and logs Warning every 100 drops per device)
            }
        }
    }
}
