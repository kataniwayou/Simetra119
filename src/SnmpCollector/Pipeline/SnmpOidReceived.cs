using Lextm.SharpSnmpLib;
using MediatR;
using System.Net;

namespace SnmpCollector.Pipeline;

/// <summary>
/// MediatR request published for every SNMP OID value received, whether from polling or a trap.
/// Implements IRequest&lt;Unit&gt; (not INotification) so that registered IPipelineBehavior pipeline
/// behaviors execute in the correct order: Logging → Exception → Validation → OidResolution → handler.
/// Sealed class (not record) so behaviors can enrich properties in-place as the pipeline progresses.
/// </summary>
public sealed class SnmpOidReceived : IRequest<Unit>
{
    /// <summary>Raw OID string from the SNMP PDU (e.g., "1.3.6.1.2.1.1.1.0").</summary>
    public required string Oid { get; init; }

    /// <summary>
    /// IP address of the SNMP agent that sent this data.
    /// Uses <c>set</c> (not <c>init</c>) because the trap path may update the address after construction.
    /// </summary>
    public required IPAddress AgentIp { get; set; }

    /// <summary>
    /// Human-readable device name. Set by the poll path at publish time; null for traps until resolved
    /// by a pipeline behavior.
    /// </summary>
    public string? DeviceName { get; set; }

    /// <summary>SharpSnmpLib typed value wrapper carrying the OID's current value.</summary>
    public required ISnmpData Value { get; init; }

    /// <summary>Distinguishes whether this event originated from a scheduled poll or an inbound trap.</summary>
    public required SnmpSource Source { get; init; }

    /// <summary>
    /// SharpSnmpLib type code for fast dispatch without <c>is</c>-type checks.
    /// Set at construction from <see cref="ISnmpData.TypeCode"/>.
    /// </summary>
    public required SnmpType TypeCode { get; init; }

    /// <summary>
    /// Resolved metric name (e.g., "sysUpTime"). Null until OidResolutionBehavior runs and maps the OID.
    /// </summary>
    public string? MetricName { get; set; }
}
