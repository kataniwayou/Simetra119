using Lextm.SharpSnmpLib;
using System.Net;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Abstraction over the static <see cref="Lextm.SharpSnmpLib.Messaging.Messenger.GetAsync"/> method.
/// Wrapping the static call in an interface makes <see cref="Jobs.MetricPollJob"/> unit-testable
/// without real UDP sockets or a live SNMP agent.
/// </summary>
public interface ISnmpClient
{
    /// <summary>
    /// Performs an SNMP GET request and returns the response varbinds.
    /// </summary>
    Task<IList<Variable>> GetAsync(
        VersionCode version,
        IPEndPoint endpoint,
        OctetString community,
        IList<Variable> variables,
        CancellationToken ct);

    /// <summary>
    /// Performs an SNMP SET request and returns the response varbinds.
    /// </summary>
    Task<IList<Variable>> SetAsync(
        VersionCode version,
        IPEndPoint endpoint,
        OctetString community,
        Variable variable,
        CancellationToken ct);
}
