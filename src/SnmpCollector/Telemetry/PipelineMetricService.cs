using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SnmpCollector.Telemetry;

/// <summary>
/// Singleton service that owns all 10 pipeline counter instruments on the SnmpCollector meter.
/// Creating counters here (once) avoids duplicate instrument registration and provides a single
/// injection point for all pipeline behaviors and handlers that need to record metrics.
/// </summary>
public sealed class PipelineMetricService : IDisposable
{
    private readonly Meter _meter;

    // PMET-01: counts every SnmpOidReceived notification published into the MediatR pipeline
    private readonly Counter<long> _published;

    // PMET-02: counts every notification that reached a terminal handler without error
    private readonly Counter<long> _handled;

    // PMET-03: counts pipeline errors (exceptions in behaviors or handlers)
    private readonly Counter<long> _errors;

    // PMET-04: counts notifications discarded before reaching a handler (e.g., unresolved OID)
    private readonly Counter<long> _rejected;

    // PMET-05: counts scheduled poll executions
    private readonly Counter<long> _pollExecuted;

    // PMET-06: counts inbound trap messages received by the listener
    private readonly Counter<long> _trapReceived;

    // PMET-07: counts traps dropped due to community string authentication failure
    private readonly Counter<long> _trapAuthFailed;

    // PMET-09: counts varbind envelopes dropped from per-device BoundedChannel (backpressure)
    private readonly Counter<long> _trapDropped;

    // Phase 6: counts transitions from healthy to unreachable (3 consecutive failures)
    private readonly Counter<long> _pollUnreachable;

    // Phase 6: counts transitions from unreachable back to healthy (first success after unreachable)
    private readonly Counter<long> _pollRecovered;

    // Phase 27: counts successful fan-out writes to tenant vector metric slots
    private readonly Counter<long> _tenantVectorRouted;

    public PipelineMetricService(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(TelemetryConstants.MeterName);

        _published = _meter.CreateCounter<long>("snmp.event.published");
        _handled = _meter.CreateCounter<long>("snmp.event.handled");
        _errors = _meter.CreateCounter<long>("snmp.event.errors");
        _rejected = _meter.CreateCounter<long>("snmp.event.rejected");
        _pollExecuted = _meter.CreateCounter<long>("snmp.poll.executed");
        _trapReceived = _meter.CreateCounter<long>("snmp.trap.received");
        _trapAuthFailed    = _meter.CreateCounter<long>("snmp.trap.auth_failed");
        _trapDropped       = _meter.CreateCounter<long>("snmp.trap.dropped");

        _pollUnreachable = _meter.CreateCounter<long>("snmp.poll.unreachable");
        _pollRecovered   = _meter.CreateCounter<long>("snmp.poll.recovered");

        _tenantVectorRouted = _meter.CreateCounter<long>("snmp.tenantvector.routed");
    }

    /// <summary>PMET-01: Increment the count of published pipeline notifications by 1.</summary>
    public void IncrementPublished(string deviceName)
        => _published.Add(1, new TagList { { "device_name", deviceName } });

    /// <summary>PMET-02: Increment the count of successfully handled notifications by 1.</summary>
    public void IncrementHandled(string deviceName)
        => _handled.Add(1, new TagList { { "device_name", deviceName } });

    /// <summary>PMET-03: Increment the count of pipeline errors by 1.</summary>
    public void IncrementErrors(string deviceName)
        => _errors.Add(1, new TagList { { "device_name", deviceName } });

    /// <summary>PMET-04: Increment the count of rejected (discarded) notifications by 1.</summary>
    public void IncrementRejected(string deviceName)
        => _rejected.Add(1, new TagList { { "device_name", deviceName } });

    /// <summary>PMET-05: Increment the count of executed poll cycles by 1.</summary>
    public void IncrementPollExecuted(string deviceName)
        => _pollExecuted.Add(1, new TagList { { "device_name", deviceName } });

    /// <summary>PMET-06: Increment the count of received trap messages by 1.</summary>
    public void IncrementTrapReceived(string deviceName)
        => _trapReceived.Add(1, new TagList { { "device_name", deviceName } });

    /// <summary>
    /// PMET-07: Increment the count of traps rejected due to community string mismatch by 1.
    /// Fired when an inbound trap's community string does not match the Simetra.{DeviceName} convention.
    /// Used for Prometheus alerting on authentication anomalies.
    /// </summary>
    public void IncrementTrapAuthFailed(string deviceName)
        => _trapAuthFailed.Add(1, new TagList { { "device_name", deviceName } });

    /// <summary>
    /// PMET-09: Increment the count of dropped varbind envelopes for the given device by 1.
    /// Fired when a device's BoundedChannel is full and DropOldest evicts an item.
    /// Includes device_name tag to identify which device is generating the trap storm.
    /// </summary>
    public void IncrementTrapDropped(string deviceName)
        => _trapDropped.Add(1, new TagList { { "device_name", deviceName } });

    /// <summary>
    /// Phase 6: Increment the count of devices transitioning to unreachable state by 1.
    /// Fired when a device reaches the consecutive failure threshold (3 failures).
    /// </summary>
    public void IncrementPollUnreachable(string deviceName)
        => _pollUnreachable.Add(1, new TagList { { "device_name", deviceName } });

    /// <summary>
    /// Phase 6: Increment the count of devices recovering from unreachable state by 1.
    /// Fired when a previously unreachable device responds successfully.
    /// </summary>
    public void IncrementPollRecovered(string deviceName)
        => _pollRecovered.Add(1, new TagList { { "device_name", deviceName } });

    /// <summary>OBS-02: Increment the count of tenant vector fan-out writes by 1.</summary>
    public void IncrementTenantVectorRouted(string deviceName)
        => _tenantVectorRouted.Add(1, new TagList { { "device_name", deviceName } });

    public void Dispose() => _meter.Dispose();
}
