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
/// SNMPv2c traps. Each received datagram is validated against the Simetra.{DeviceName}
/// community string convention. Device identity is extracted from the community string
/// (no device registry dependency). Authenticated varbinds are written as VarbindEnvelopes
/// to the single shared ITrapChannel for consumption by ChannelConsumerService.
///
/// ARCHITECTURAL CONSTRAINT: This class NEVER references ISender, IPublisher, IMediator, or
/// MediatR. All data flows through the shared channel via ITrapChannel only.
/// </summary>
public sealed class SnmpTrapListenerService : BackgroundService
{
    private readonly ITrapChannel _trapChannel;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly SnmpListenerOptions _listenerOptions;
    private readonly ILogger<SnmpTrapListenerService> _logger;

    /// <summary>
    /// SharpSnmpLib requires a UserRegistry even for SNMPv2c (no USM users needed).
    /// Creating it once avoids repeated allocation per datagram.
    /// </summary>
    private readonly UserRegistry _userRegistry = new();

    /// <summary>
    /// Indicates whether the UDP socket has been successfully bound.
    /// Used by ReadinessHealthCheck (Plan 04) to verify trap listener is operational.
    /// </summary>
    private volatile bool _isBound;

    /// <summary>
    /// Public property for ReadinessHealthCheck to verify trap listener bound status.
    /// </summary>
    public bool IsBound => _isBound;

    public SnmpTrapListenerService(
        ITrapChannel trapChannel,
        PipelineMetricService pipelineMetrics,
        IOptions<SnmpListenerOptions> listenerOptions,
        ILogger<SnmpTrapListenerService> logger)
    {
        _trapChannel = trapChannel;
        _pipelineMetrics = pipelineMetrics;
        _listenerOptions = listenerOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Binds the UDP socket and enters the receive loop until cancellation is requested.
    /// Each datagram is processed synchronously via ProcessDatagram -- TryWrite is non-blocking.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bindAddress = IPAddress.Parse(_listenerOptions.BindAddress);
        var endpoint = new IPEndPoint(bindAddress, _listenerOptions.Port);

        using var udpClient = new UdpClient(endpoint);
        _isBound = true;

        _logger.LogInformation(
            "Trap listener bound to UDP {Port}",
            _listenerOptions.Port);

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
    /// Overrides StopAsync to signal the shared channel to complete after the receive loop exits.
    /// Order: base.StopAsync cancels ExecuteAsync first, then Complete signals consumer.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        _trapChannel.Complete();
    }

    /// <summary>
    /// Parses a UDP datagram, validates the Simetra.* community string convention,
    /// extracts device name from the community string, and routes each varbind to the
    /// shared channel. Invalid community strings are dropped with Debug log.
    /// Internal for unit testing via InternalsVisibleTo.
    /// </summary>
    internal void ProcessDatagram(UdpReceiveResult result)
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

            // Normalize IPv6-mapped IPv4 addresses (e.g., ::ffff:192.168.1.1 -> 192.168.1.1)
            var senderIp = result.RemoteEndPoint.Address.MapToIPv4();
            var receivedCommunity = trapV2.Community().ToString();

            // Validate Simetra.* convention and extract device name
            if (!CommunityStringHelper.TryExtractDeviceName(receivedCommunity, out var deviceName))
            {
                _logger.LogDebug(
                    "Trap dropped: invalid community string from {SourceIp}",
                    senderIp);
                _pipelineMetrics.IncrementTrapAuthFailed("unknown");
                continue;
            }

            // Route each varbind to the shared channel
            var variables = trapV2.Variables();
            foreach (var variable in variables)
            {
                var envelope = new VarbindEnvelope(
                    Oid: variable.Id.ToString(),
                    Value: variable.Data,
                    TypeCode: variable.Data.TypeCode,
                    AgentIp: senderIp,
                    DeviceName: deviceName);

                _trapChannel.Writer.TryWrite(envelope);
            }
        }
    }
}
