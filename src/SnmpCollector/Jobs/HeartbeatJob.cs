using System.Net;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;

namespace SnmpCollector.Jobs;

/// <summary>
/// Sends a loopback SNMP v2c trap to the listener using the heartbeat OID from
/// <see cref="HeartbeatJobOptions.HeartbeatOid"/>, proving the scheduler is alive.
/// The trap flows through the full pipeline (listener -> middleware -> extraction -> processing)
/// exactly like any external device trap. Stamps liveness vector on completion.
/// </summary>
[DisallowConcurrentExecution]
public sealed class HeartbeatJob : IJob
{
    private readonly ICorrelationService _correlation;
    private readonly ILivenessVectorService _liveness;
    private readonly int _listenerPort;
    private readonly string _communityString;
    private readonly ILogger<HeartbeatJob> _logger;

    public HeartbeatJob(
        ICorrelationService correlation,
        ILivenessVectorService liveness,
        IOptions<SnmpListenerOptions> listenerOptions,
        ILogger<HeartbeatJob> logger)
    {
        _correlation = correlation;
        _liveness = liveness;
        _listenerPort = listenerOptions.Value.Port;
        _communityString = CommunityStringHelper.DeriveFromDeviceName(HeartbeatJobOptions.HeartbeatDeviceName);
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _correlation.OperationCorrelationId = _correlation.CurrentCorrelationId;
        var jobKey = context.JobDetail.Key.Name;

        try
        {
            var variables = new List<Variable>
            {
                new(new ObjectIdentifier(HeartbeatJobOptions.HeartbeatOid), new Integer32(1))
            };

            var receiver = new IPEndPoint(IPAddress.Loopback, _listenerPort);

            await Task.Run(() => Messenger.SendTrapV2(
                requestId: 0,
                version: VersionCode.V2,
                receiver: receiver,
                community: new OctetString(_communityString),
                enterprise: new ObjectIdentifier(HeartbeatJobOptions.HeartbeatOid),
                timestamp: 0,
                variables: variables));

            _logger.LogDebug(
                "Heartbeat trap sent to 127.0.0.1:{ListenerPort}",
                _listenerPort);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Heartbeat job {JobKey} failed",
                jobKey);
        }
        finally
        {
            _correlation.OperationCorrelationId = null;
            _liveness.Stamp(jobKey);
        }
    }
}
