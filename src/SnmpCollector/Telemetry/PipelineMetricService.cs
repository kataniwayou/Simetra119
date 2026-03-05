using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;

namespace SnmpCollector.Telemetry;

/// <summary>
/// Singleton service that owns all 9 pipeline counter instruments on the SnmpCollector meter.
/// Creating counters here (once) avoids duplicate instrument registration and provides a single
/// injection point for all pipeline behaviors and handlers that need to record metrics.
/// </summary>
public sealed class PipelineMetricService : IDisposable
{
    private readonly Meter _meter;
    private readonly string _siteName;

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

    // PMET-08: counts traps received from IPs not in the device registry
    private readonly Counter<long> _trapUnknownDevice;

    // PMET-09: counts varbind envelopes dropped from per-device BoundedChannel (backpressure)
    private readonly Counter<long> _trapDropped;

    public PipelineMetricService(IMeterFactory meterFactory, IOptions<SiteOptions> siteOptions)
    {
        _meter = meterFactory.Create(TelemetryConstants.MeterName);
        _siteName = siteOptions.Value.Name;

        _published = _meter.CreateCounter<long>("snmp.event.published");
        _handled = _meter.CreateCounter<long>("snmp.event.handled");
        _errors = _meter.CreateCounter<long>("snmp.event.errors");
        _rejected = _meter.CreateCounter<long>("snmp.event.rejected");
        _pollExecuted = _meter.CreateCounter<long>("snmp.poll.executed");
        _trapReceived = _meter.CreateCounter<long>("snmp.trap.received");
        _trapAuthFailed    = _meter.CreateCounter<long>("snmp.trap.auth_failed");
        _trapUnknownDevice = _meter.CreateCounter<long>("snmp.trap.unknown_device");
        _trapDropped       = _meter.CreateCounter<long>("snmp.trap.dropped");
    }

    /// <summary>PMET-01: Increment the count of published pipeline notifications by 1.</summary>
    public void IncrementPublished()
        => _published.Add(1, new TagList { { "site_name", _siteName } });

    /// <summary>PMET-02: Increment the count of successfully handled notifications by 1.</summary>
    public void IncrementHandled()
        => _handled.Add(1, new TagList { { "site_name", _siteName } });

    /// <summary>PMET-03: Increment the count of pipeline errors by 1.</summary>
    public void IncrementErrors()
        => _errors.Add(1, new TagList { { "site_name", _siteName } });

    /// <summary>PMET-04: Increment the count of rejected (discarded) notifications by 1.</summary>
    public void IncrementRejected()
        => _rejected.Add(1, new TagList { { "site_name", _siteName } });

    /// <summary>PMET-05: Increment the count of executed poll cycles by 1.</summary>
    public void IncrementPollExecuted()
        => _pollExecuted.Add(1, new TagList { { "site_name", _siteName } });

    /// <summary>PMET-06: Increment the count of received trap messages by 1.</summary>
    public void IncrementTrapReceived()
        => _trapReceived.Add(1, new TagList { { "site_name", _siteName } });

    /// <summary>
    /// PMET-07: Increment the count of traps rejected due to community string mismatch by 1.
    /// Fired when an inbound trap's community string does not match the device-specific or
    /// global community string. Used for Prometheus alerting on authentication anomalies.
    /// </summary>
    public void IncrementTrapAuthFailed()
        => _trapAuthFailed.Add(1, new TagList { { "site_name", _siteName } });

    /// <summary>
    /// PMET-08: Increment the count of traps from unregistered source IPs by 1.
    /// Fired when the sender IP does not resolve to any device in the device registry.
    /// Indicates rogue or misconfigured devices sending traps to this collector.
    /// </summary>
    public void IncrementTrapUnknownDevice()
        => _trapUnknownDevice.Add(1, new TagList { { "site_name", _siteName } });

    /// <summary>
    /// PMET-09: Increment the count of dropped varbind envelopes for the given device by 1.
    /// Fired when a device's BoundedChannel is full and DropOldest evicts an item.
    /// Includes device_name tag to identify which device is generating the trap storm.
    /// </summary>
    public void IncrementTrapDropped(string deviceName)
        => _trapDropped.Add(1, new TagList { { "site_name", _siteName }, { "device_name", deviceName } });

    public void Dispose() => _meter.Dispose();
}
