using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;
using SnmpCollector.Tests.Helpers;
using Xunit;

namespace SnmpCollector.Tests.Telemetry;

/// <summary>
/// Unit tests for all 8 instruments on <see cref="TenantMetricService"/>.
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
    private readonly List<(string InstrumentName, long Value, KeyValuePair<string, object?>[] Tags)> _measurements = new();
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
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            _measurements.Add((instrument.Name, value, tags.ToArray()));
        });
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
    // 1. IncrementTier1Stale records with tenant_id and priority tags
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementTier1Stale_RecordsWithTenantIdAndPriorityTags()
    {
        _service.IncrementTier1Stale("tenant-a", 1);

        var match = _measurements.Single(m => m.InstrumentName == "tenant.tier1.stale");

        Assert.Equal(1L, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("tenant-a", tags["tenant_id"]);
        Assert.Equal(1, tags["priority"]);
        Assert.DoesNotContain("device_name", tags.Keys);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 2. IncrementTier2Resolved records with tenant_id and priority tags
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementTier2Resolved_RecordsWithTenantIdAndPriorityTags()
    {
        _service.IncrementTier2Resolved("tenant-a", 1);

        var match = _measurements.Single(m => m.InstrumentName == "tenant.tier2.resolved");

        Assert.Equal(1L, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("tenant-a", tags["tenant_id"]);
        Assert.Equal(1, tags["priority"]);
        Assert.DoesNotContain("device_name", tags.Keys);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 3. IncrementTier3Evaluate records with tenant_id and priority tags
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementTier3Evaluate_RecordsWithTenantIdAndPriorityTags()
    {
        _service.IncrementTier3Evaluate("tenant-a", 1);

        var match = _measurements.Single(m => m.InstrumentName == "tenant.tier3.evaluate");

        Assert.Equal(1L, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("tenant-a", tags["tenant_id"]);
        Assert.Equal(1, tags["priority"]);
        Assert.DoesNotContain("device_name", tags.Keys);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 4. IncrementCommandDispatched records with tenant_id and priority tags
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementCommandDispatched_RecordsWithTenantIdAndPriorityTags()
    {
        _service.IncrementCommandDispatched("tenant-a", 1);

        var match = _measurements.Single(m => m.InstrumentName == "tenant.command.dispatched");

        Assert.Equal(1L, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("tenant-a", tags["tenant_id"]);
        Assert.Equal(1, tags["priority"]);
        Assert.DoesNotContain("device_name", tags.Keys);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 5. IncrementCommandFailed records with tenant_id and priority tags
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementCommandFailed_RecordsWithTenantIdAndPriorityTags()
    {
        _service.IncrementCommandFailed("tenant-a", 1);

        var match = _measurements.Single(m => m.InstrumentName == "tenant.command.failed");

        Assert.Equal(1L, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("tenant-a", tags["tenant_id"]);
        Assert.Equal(1, tags["priority"]);
        Assert.DoesNotContain("device_name", tags.Keys);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 6. IncrementCommandSuppressed records with tenant_id and priority tags
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementCommandSuppressed_RecordsWithTenantIdAndPriorityTags()
    {
        _service.IncrementCommandSuppressed("tenant-a", 1);

        var match = _measurements.Single(m => m.InstrumentName == "tenant.command.suppressed");

        Assert.Equal(1L, match.Value);
        var tags = match.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("tenant-a", tags["tenant_id"]);
        Assert.Equal(1, tags["priority"]);
        Assert.DoesNotContain("device_name", tags.Keys);
        Assert.DoesNotContain("host_name", tags.Keys);
        Assert.DoesNotContain("pod_name", tags.Keys);
    }

    // -----------------------------------------------------------------------
    // 7. RecordTenantState records enum value as double with tags
    // -----------------------------------------------------------------------

    [Fact]
    public void RecordTenantState_RecordsEnumValueWithTags()
    {
        _service.RecordTenantState("tenant-a", 1, TenantState.Unresolved);

        var match = _doubleMeasurements.Single(m => m.InstrumentName == "tenant.state");

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
}
