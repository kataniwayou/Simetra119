namespace SnmpCollector.Pipeline;

/// <summary>
/// Minimal record carrying the data needed to execute an SNMP SET command.
/// <para>
/// CommunityString is intentionally excluded -- it is resolved at execution time
/// from <see cref="IDeviceRegistry"/> by the CommandWorkerService, ensuring that
/// community strings are never cached in the channel buffer and always reflect
/// the latest device configuration.
/// </para>
/// </summary>
public sealed record CommandRequest(
    string Ip,
    int Port,
    string CommandName,
    string Value,
    string ValueType,
    string DeviceName);
