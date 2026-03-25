using System.Reflection;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace SnmpCollector.Telemetry;

/// <summary>
/// Metric-specific role-gated exporter that gates only metrics from a specific meter
/// (e.g., "SnmpCollector.Leader") behind leader election, while allowing all other meters
/// (e.g., System.Runtime, SnmpCollector) to export on every instance regardless of role.
/// This ensures runtime and pipeline metrics are always exported by both leader and follower
/// pods for operational visibility, while business metrics (snmp_gauge, snmp_info)
/// are exported only by the leader.
/// </summary>
public sealed class MetricRoleGatedExporter : BaseExporter<Metric>
{
    private static readonly PropertyInfo ParentProviderProperty =
        typeof(BaseExporter<Metric>).GetProperty("ParentProvider")!;

    private readonly BaseExporter<Metric> _inner;
    private readonly ILeaderElection _leaderElection;
    private readonly string _gatedMeterName;
    private bool _parentProviderPropagated;

    public MetricRoleGatedExporter(
        BaseExporter<Metric> inner,
        ILeaderElection leaderElection,
        string gatedMeterName)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _leaderElection = leaderElection ?? throw new ArgumentNullException(nameof(leaderElection));
        _gatedMeterName = gatedMeterName ?? throw new ArgumentNullException(nameof(gatedMeterName));
    }

    public override ExportResult Export(in Batch<Metric> batch)
    {
        // Propagate ParentProvider to inner exporter so OTLP export includes
        // resource attributes (service.name, service.instance.id).
        // ParentProvider has an internal setter, so reflection is required.
        if (!_parentProviderPropagated && ParentProvider != null)
        {
            ParentProviderProperty.SetValue(_inner, ParentProvider);
            _parentProviderPropagated = true;
        }

        if (_leaderElection.IsLeader)
        {
            // Leader: export everything (business + pipeline + runtime)
            return _inner.Export(batch);
        }

        // Follower: filter out the gated business meter, pass through everything else
        var ungated = new List<Metric>();
        foreach (var metric in batch)
        {
            if (!string.Equals(metric.MeterName, _gatedMeterName, StringComparison.Ordinal))
            {
                ungated.Add(metric);
            }
        }

        if (ungated.Count == 0)
            return ExportResult.Success; // Return Success not Failure — no retry needed for intentionally suppressed data

        var filteredBatch = new Batch<Metric>(ungated.ToArray(), ungated.Count);
        return _inner.Export(filteredBatch);
    }

    protected override bool OnForceFlush(int timeoutMilliseconds)
    {
        return _inner.ForceFlush(timeoutMilliseconds);
    }

    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        return _inner.Shutdown(timeoutMilliseconds);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
