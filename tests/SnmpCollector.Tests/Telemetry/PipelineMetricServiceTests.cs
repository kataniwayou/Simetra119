using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using SnmpCollector.Telemetry;
using SnmpCollector.Tests.Helpers;
using Xunit;

namespace SnmpCollector.Tests.Telemetry;

/// <summary>
/// Unit tests for the pipeline counter methods on <see cref="PipelineMetricService"/>.
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
    // 1. IncrementTrapAuthFailed records with device_name tag (PMET-07)
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementTrapAuthFailed_RecordsWithDeviceNameTag()
    {
        _service.IncrementTrapAuthFailed("test-device");

        var match = _measurements.Single(m => m.InstrumentName == "snmp.trap.auth_failed");

        Assert.Equal(1L, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("test-device", tags["device_name"]);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 2. IncrementTrapDropped records with device_name tag (PMET-09)
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementTrapDropped_RecordsWithDeviceNameTag()
    {
        _service.IncrementTrapDropped("router-01");

        var match = _measurements.Single(m => m.InstrumentName == "snmp.trap.dropped");

        Assert.Equal(1L, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("router-01", tags["device_name"]);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 4. IncrementTrapReceived records with device_name tag (PMET-06)
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementTrapReceived_RecordsWithDeviceNameTag()
    {
        _service.IncrementTrapReceived("test-device");

        var match = _measurements.Single(m => m.InstrumentName == "snmp.trap.received");

        Assert.Equal(1L, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("test-device", tags["device_name"]);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 5. IncrementCommandSent records with device_name tag (PMET-13)
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementCommandSent_RecordsWithDeviceNameTag()
    {
        _service.IncrementCommandSent("device-01");

        var match = _measurements.Single(m => m.InstrumentName == "snmp.command.sent");

        Assert.Equal(1L, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("device-01", tags["device_name"]);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 6. IncrementCommandFailed records with device_name tag (PMET-14)
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementCommandFailed_RecordsWithDeviceNameTag()
    {
        _service.IncrementCommandFailed("device-01");

        var match = _measurements.Single(m => m.InstrumentName == "snmp.command.failed");

        Assert.Equal(1L, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("device-01", tags["device_name"]);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 7. IncrementCommandSuppressed records with device_name tag (PMET-15)
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementCommandSuppressed_RecordsWithDeviceNameTag()
    {
        _service.IncrementCommandSuppressed("device-01");

        var match = _measurements.Single(m => m.InstrumentName == "snmp.command.suppressed");

        Assert.Equal(1L, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("device-01", tags["device_name"]);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }
}
