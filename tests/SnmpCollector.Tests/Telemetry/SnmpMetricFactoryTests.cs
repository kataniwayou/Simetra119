using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using SnmpCollector.Telemetry;
using SnmpCollector.Tests.Helpers;
using Xunit;

namespace SnmpCollector.Tests.Telemetry;

/// <summary>
/// Tests the real <see cref="SnmpMetricFactory"/> to verify OTel instrument tags
/// that are not observable through <see cref="ISnmpMetricFactory"/> (e.g., host_name).
/// Uses <see cref="MeterListener"/> to capture actual instrument recordings.
/// Placed in NonParallelMeterTests collection to prevent cross-test meter contamination.
/// </summary>
[Collection(NonParallelCollection.Name)]
public sealed class SnmpMetricFactoryTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly SnmpMetricFactory _factory;
    private readonly MeterListener _listener;
    private readonly List<KeyValuePair<string, object?>[]> _recordedTags = new();

    public SnmpMetricFactoryTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        _sp = services.BuildServiceProvider();

        _factory = new SnmpMetricFactory(
            _sp.GetRequiredService<IMeterFactory>());

        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TelemetryConstants.LeaderMeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            _recordedTags.Add(tags.ToArray());
        });
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _factory.Dispose();
        _sp.Dispose();
    }

    [Fact]
    public void RecordGauge_IncludesAllSevenLabels()
    {
        _factory.RecordGauge("hrProcessorLoad", "1.3.6.1.2.1.25.3.3.1.2", "core-router", "10.0.0.1", "poll", "integer32", 42.0);

        Assert.Single(_recordedTags);
        var tags = _recordedTags[0].ToDictionary(t => t.Key, t => t.Value);

        Assert.True(tags.ContainsKey("host_name"));
        Assert.Equal("hrProcessorLoad", tags["metric_name"]);
        Assert.Equal("1.3.6.1.2.1.25.3.3.1.2", tags["oid"]);
        Assert.Equal("core-router", tags["device_name"]);
        Assert.Equal("10.0.0.1", tags["ip"]);
        Assert.Equal("poll", tags["source"]);
        Assert.Equal("integer32", tags["snmp_type"]);
        Assert.Equal(7, tags.Count);
    }

    [Fact]
    public void RecordInfo_IncludesAllEightLabels()
    {
        _factory.RecordInfo("sysDescr", "1.3.6.1.2.1.1.1.0", "test-device", "10.0.0.2", "trap", "octetstring", "Linux router");

        Assert.Single(_recordedTags);
        var tags = _recordedTags[0].ToDictionary(t => t.Key, t => t.Value);

        Assert.True(tags.ContainsKey("host_name"));
        Assert.Equal("sysDescr", tags["metric_name"]);
        Assert.Equal("1.3.6.1.2.1.1.1.0", tags["oid"]);
        Assert.Equal("test-device", tags["device_name"]);
        Assert.Equal("10.0.0.2", tags["ip"]);
        Assert.Equal("trap", tags["source"]);
        Assert.Equal("octetstring", tags["snmp_type"]);
        Assert.Equal("Linux router", tags["value"]);
        Assert.Equal(8, tags.Count);
    }

    [Fact]
    public void RecordInfo_TruncatesLongValueAt128Chars()
    {
        var longValue = new string('x', 200);

        _factory.RecordInfo("sysDescr", "1.3.6.1.2.1.1.1.0", "test-device", "10.0.0.1", "poll", "octetstring", longValue);

        Assert.Single(_recordedTags);
        var tags = _recordedTags[0].ToDictionary(t => t.Key, t => t.Value);
        var valueStr = (string)tags["value"]!;
        Assert.Equal(128, valueStr.Length);
        Assert.EndsWith("...", valueStr);
    }
}
