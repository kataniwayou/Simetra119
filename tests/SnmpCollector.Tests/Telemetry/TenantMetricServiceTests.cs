using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;
using SnmpCollector.Tests.Helpers;
using Xunit;

namespace SnmpCollector.Tests.Telemetry;

/// <summary>
/// Unit tests for all 8 instruments on <see cref="TenantMetricService"/>:
/// 6 percentage gauges, 1 state gauge, 1 duration histogram.
/// Uses <see cref="MeterListener"/> to observe actual OTel measurements and tag values.
/// Placed in NonParallelCollection to prevent cross-test meter contamination
/// (MeterListener is a global listener; parallel tests with the same meter name interfere).
/// </summary>
[Collection(NonParallelCollection.Name)]
public sealed class TenantMetricServiceTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly TenantMetricService _service;
    private readonly MeterListener _listener;

    // Recorded measurements: (instrumentName, value, tags)
    private readonly List<(string InstrumentName, double Value, KeyValuePair<string, object?>[] Tags)> _doubleMeasurements = new();

    public TenantMetricServiceTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        _sp = services.BuildServiceProvider();

        _service = new TenantMetricService(
            _sp.GetRequiredService<IMeterFactory>());

        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TelemetryConstants.TenantMeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            _doubleMeasurements.Add((instrument.Name, value, tags.ToArray()));
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
    // 1. RecordMetricStalePercent records gauge value with tenant_id and priority tags
    // -----------------------------------------------------------------------

    [Fact]
    public void RecordMetricStalePercent_RecordsWithTenantIdAndPriorityTags()
    {
        _service.RecordMetricStalePercent("tenant-a", 1, 50.0);

        var match = _doubleMeasurements.Single(m => m.InstrumentName == "tenant.metric.stale.percent");

        Assert.Equal(50.0, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("tenant-a", tags["tenant_id"]);
        Assert.Equal(1, tags["priority"]);
        Assert.DoesNotContain("device_name", tags.Keys);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 2. RecordMetricResolvedPercent records gauge value with tenant_id and priority tags
    // -----------------------------------------------------------------------

    [Fact]
    public void RecordMetricResolvedPercent_RecordsWithTenantIdAndPriorityTags()
    {
        _service.RecordMetricResolvedPercent("tenant-a", 1, 75.0);

        var match = _doubleMeasurements.Single(m => m.InstrumentName == "tenant.metric.resolved.percent");

        Assert.Equal(75.0, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("tenant-a", tags["tenant_id"]);
        Assert.Equal(1, tags["priority"]);
        Assert.DoesNotContain("device_name", tags.Keys);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 3. RecordMetricEvaluatePercent records gauge value with tenant_id and priority tags
    // -----------------------------------------------------------------------

    [Fact]
    public void RecordMetricEvaluatePercent_RecordsWithTenantIdAndPriorityTags()
    {
        _service.RecordMetricEvaluatePercent("tenant-a", 1, 33.33);

        var match = _doubleMeasurements.Single(m => m.InstrumentName == "tenant.metric.evaluate.percent");

        Assert.Equal(33.33, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("tenant-a", tags["tenant_id"]);
        Assert.Equal(1, tags["priority"]);
        Assert.DoesNotContain("device_name", tags.Keys);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 4. RecordCommandDispatchedPercent records gauge value with tenant_id and priority tags
    // -----------------------------------------------------------------------

    [Fact]
    public void RecordCommandDispatchedPercent_RecordsWithTenantIdAndPriorityTags()
    {
        _service.RecordCommandDispatchedPercent("tenant-a", 1, 60.0);

        var match = _doubleMeasurements.Single(m => m.InstrumentName == "tenant.command.dispatched.percent");

        Assert.Equal(60.0, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("tenant-a", tags["tenant_id"]);
        Assert.Equal(1, tags["priority"]);
        Assert.DoesNotContain("device_name", tags.Keys);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 5. RecordCommandFailedPercent records gauge value with tenant_id and priority tags
    // -----------------------------------------------------------------------

    [Fact]
    public void RecordCommandFailedPercent_RecordsWithTenantIdAndPriorityTags()
    {
        _service.RecordCommandFailedPercent("tenant-a", 1, 25.0);

        var match = _doubleMeasurements.Single(m => m.InstrumentName == "tenant.command.failed.percent");

        Assert.Equal(25.0, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("tenant-a", tags["tenant_id"]);
        Assert.Equal(1, tags["priority"]);
        Assert.DoesNotContain("device_name", tags.Keys);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 6. RecordCommandSuppressedPercent records gauge value with tenant_id and priority tags
    // -----------------------------------------------------------------------

    [Fact]
    public void RecordCommandSuppressedPercent_RecordsWithTenantIdAndPriorityTags()
    {
        _service.RecordCommandSuppressedPercent("tenant-a", 1, 10.0);

        var match = _doubleMeasurements.Single(m => m.InstrumentName == "tenant.command.suppressed.percent");

        Assert.Equal(10.0, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("tenant-a", tags["tenant_id"]);
        Assert.Equal(1, tags["priority"]);
        Assert.DoesNotContain("device_name", tags.Keys);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 7. RecordTenantState records enum value as double with renamed instrument
    // -----------------------------------------------------------------------

    [Fact]
    public void RecordTenantState_RecordsEnumValueWithTags()
    {
        _service.RecordTenantState("tenant-a", 1, TenantState.Unresolved);

        var match = _doubleMeasurements.Single(m => m.InstrumentName == "tenant.evaluation.state");

        Assert.Equal(3.0, match.Value); // Unresolved = 3
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("tenant-a", tags["tenant_id"]);
        Assert.Equal(1, tags["priority"]);
        Assert.DoesNotContain("device_name", tags.Keys);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 8. RecordEvaluationDuration records histogram with tags
    // -----------------------------------------------------------------------

    [Fact]
    public void RecordEvaluationDuration_RecordsMillisecondsWithTags()
    {
        _service.RecordEvaluationDuration("tenant-a", 1, 42.5);

        var match = _doubleMeasurements.Single(m => m.InstrumentName == "tenant.evaluation.duration.milliseconds");

        Assert.Equal(42.5, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("tenant-a", tags["tenant_id"]);
        Assert.Equal(1, tags["priority"]);
        Assert.DoesNotContain("device_name", tags.Keys);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 9. Zero percent edge case: RecordMetricStalePercent with 0.0 records zero
    // -----------------------------------------------------------------------

    [Fact]
    public void RecordMetricStalePercent_ZeroPercent_RecordsZero()
    {
        _service.RecordMetricStalePercent("tenant-a", 1, 0.0);

        var match = _doubleMeasurements.Single(m => m.InstrumentName == "tenant.metric.stale.percent");

        Assert.Equal(0.0, match.Value);
    }
}
