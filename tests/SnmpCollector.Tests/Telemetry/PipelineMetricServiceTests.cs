using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using SnmpCollector.Telemetry;
using SnmpCollector.Tests.Helpers;
using Xunit;

namespace SnmpCollector.Tests.Telemetry;

/// <summary>
/// Unit tests for the Phase 5 trap counter methods on <see cref="PipelineMetricService"/>.
/// Uses <see cref="MeterListener"/> to observe actual OTel counter increments and tag values.
/// Placed in NonParallelMeterTests collection to prevent cross-test meter contamination
/// (MeterListener is a global listener; parallel tests with the same meter name interfere).
/// </summary>
[Collection(NonParallelCollection.Name)]
public sealed class PipelineMetricServiceTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly PipelineMetricService _service;
    private readonly MeterListener _listener;

    // Recorded measurements: (instrumentName, value, tags)
    private readonly List<(string InstrumentName, long Value, KeyValuePair<string, object?>[] Tags)> _measurements = new();

    public PipelineMetricServiceTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        _sp = services.BuildServiceProvider();

        _service = new PipelineMetricService(
            _sp.GetRequiredService<IMeterFactory>());

        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TelemetryConstants.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            _measurements.Add((instrument.Name, value, tags.ToArray()));
        });
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _service.Dispose();
        _sp.Dispose();
    }

    // -----------------------------------------------------------------------
    // 1. IncrementTrapAuthFailed_RecordsWithHostNameTag (PMET-07)
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementTrapAuthFailed_RecordsWithHostNameTag()
    {
        _service.IncrementTrapAuthFailed();

        var match = _measurements.Single(m => m.InstrumentName == "snmp.trap.auth_failed");

        Assert.Equal(1L, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.True(tags.ContainsKey("host_name"));
        Assert.DoesNotContain("device_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 2. IncrementTrapUnknownDevice_RecordsWithHostNameTag (PMET-08)
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementTrapUnknownDevice_RecordsWithHostNameTag()
    {
        _service.IncrementTrapUnknownDevice();

        var match = _measurements.Single(m => m.InstrumentName == "snmp.trap.unknown_device");

        Assert.Equal(1L, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.True(tags.ContainsKey("host_name"));
        Assert.DoesNotContain("device_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 3. IncrementTrapDropped_RecordsWithDeviceNameTag (PMET-09)
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementTrapDropped_RecordsWithDeviceNameTag()
    {
        _service.IncrementTrapDropped("router-01");

        var match = _measurements.Single(m => m.InstrumentName == "snmp.trap.dropped");

        Assert.Equal(1L, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.True(tags.ContainsKey("host_name"));
        Assert.Equal("router-01", tags["device_name"]);
    }

    // -----------------------------------------------------------------------
    // 4. IncrementTrapReceived_RecordsWithHostNameTag (PMET-06)
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementTrapReceived_RecordsWithHostNameTag()
    {
        _service.IncrementTrapReceived();

        var match = _measurements.Single(m => m.InstrumentName == "snmp.trap.received");

        Assert.Equal(1L, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.True(tags.ContainsKey("host_name"));
    }
}
