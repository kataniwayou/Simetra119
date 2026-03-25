using Lextm.SharpSnmpLib;
using System.Net;

namespace SnmpCollector.Pipeline;

/// <summary>
/// Lightweight message written to per-device BoundedChannel by SnmpTrapListenerService.
/// Carries one varbind from a received trap, plus pre-resolved agent identity for
/// SnmpOidReceived construction. DeviceName is included because the listener already
/// resolved the device during auth — avoids double lookup in the consumer.
/// </summary>
public sealed record VarbindEnvelope(
    string Oid,
    ISnmpData Value,
    SnmpType TypeCode,
    IPAddress AgentIp,
    string DeviceName);
