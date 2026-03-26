using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using SnmpCollector.Telemetry;
using SnmpCollector.Tests.Helpers;
using Xunit;

namespace SnmpCollector.Tests.Telemetry;

/// <summary>
/// Unit tests for <see cref="MetricRoleGatedExporter"/> gating logic and OTel SDK
/// internal API stability (ParentProvider reflection breakage detection).
///
/// Uses a real OTel MeterProvider pipeline to produce <see cref="Metric"/> instances
/// from named meters, then verifies that the exporter routes them correctly based on
/// <see cref="ILeaderElection.IsLeader"/>.
///
/// Placed in NonParallelMeterTests collection because MeterProvider + PeriodicExportingMetricReader
/// interact with OTel SDK global state that can interfere across concurrent test classes.
/// </summary>
[Collection(NonParallelCollection.Name)]
public sealed class MetricRoleGatedExporterTests : IDisposable
{
    // -----------------------------------------------------------------------
    // Test infrastructure
    // -----------------------------------------------------------------------

    private sealed class StubLeaderElection : ILeaderElection
    {
        public bool IsLeader { get; set; }
        public string CurrentRole => IsLeader ? "leader" : "follower";
    }

    private sealed class CapturingExporter : BaseExporter<Metric>
    {
        public int ExportCallCount { get; private set; }
        public List<string> ExportedMeterNames { get; } = new();

        public override ExportResult Export(in Batch<Metric> batch)
        {
            ExportCallCount++;
            foreach (var metric in batch)
            {
                ExportedMeterNames.Add(metric.MeterName);
            }
            return ExportResult.Success;
        }
    }

    /// <summary>
    /// Tracks disposables that must be released after each test.
    /// </summary>
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var d in _disposables)
        {
            try { d.Dispose(); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Creates a test pipeline: CapturingExporter (innermost) wrapped by MetricRoleGatedExporter,
    /// wired into a MeterProvider with both MeterName and LeaderMeterName meters registered.
    ///
    /// The PeriodicExportingMetricReader is configured with exportIntervalMilliseconds = int.MaxValue
    /// so export only fires when ForceFlush() is called explicitly in tests.
    ///
    /// Returns meters (not factory-managed) so callers can record measurements and trigger
    /// the pipeline to collect them.
    /// </summary>
    private (CapturingExporter inner, MeterProvider provider, Meter pipelineMeter, Meter leaderMeter)
        CreateTestPipeline(StubLeaderElection leaderElection)
    {
        var inner = new CapturingExporter();
        var gated = new MetricRoleGatedExporter(inner, new Lazy<ILeaderElection>(() => leaderElection), TelemetryConstants.LeaderMeterName);
        var reader = new PeriodicExportingMetricReader(gated, exportIntervalMilliseconds: int.MaxValue);

        var provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(TelemetryConstants.MeterName)
            .AddMeter(TelemetryConstants.LeaderMeterName)
            .AddReader(reader)
            .Build()!;

        // Meters with unique names to avoid cross-test contamination within the collection.
        var pipelineMeter = new Meter(TelemetryConstants.MeterName);
        var leaderMeter = new Meter(TelemetryConstants.LeaderMeterName);

        _disposables.Add(leaderMeter);
        _disposables.Add(pipelineMeter);
        _disposables.Add(provider);

        return (inner, provider, pipelineMeter, leaderMeter);
    }

    // -----------------------------------------------------------------------
    // 1. Leader exports all metrics (both meters pass through)
    // -----------------------------------------------------------------------

    [Fact]
    public void Leader_ExportsAllMetrics()
    {
        var leaderElection = new StubLeaderElection { IsLeader = true };
        var (inner, provider, pipelineMeter, leaderMeter) = CreateTestPipeline(leaderElection);

        // Record one metric on the pipeline meter and one on the leader meter.
        pipelineMeter.CreateCounter<long>("snmp.event.published").Add(1);
        leaderMeter.CreateGauge<double>("snmp_gauge").Record(42.0);

        provider.ForceFlush();

        Assert.Contains(TelemetryConstants.MeterName, inner.ExportedMeterNames);
        Assert.Contains(TelemetryConstants.LeaderMeterName, inner.ExportedMeterNames);
    }

    // -----------------------------------------------------------------------
    // 2. Follower passes pipeline metrics but filters out leader (business) metrics
    // -----------------------------------------------------------------------

    [Fact]
    public void Follower_FiltersOutGatedMeterButPassesPipelineMetrics()
    {
        var leaderElection = new StubLeaderElection { IsLeader = false };
        var (inner, provider, pipelineMeter, leaderMeter) = CreateTestPipeline(leaderElection);

        pipelineMeter.CreateCounter<long>("snmp.event.published").Add(1);
        leaderMeter.CreateGauge<double>("snmp_gauge").Record(42.0);

        provider.ForceFlush();

        // Pipeline metrics MUST reach inner exporter.
        Assert.Contains(TelemetryConstants.MeterName, inner.ExportedMeterNames);
        // Business metrics MUST be filtered out.
        Assert.DoesNotContain(TelemetryConstants.LeaderMeterName, inner.ExportedMeterNames);
    }

    // -----------------------------------------------------------------------
    // 3. Follower with only gated metrics: inner exporter not called, returns Success
    // -----------------------------------------------------------------------

    [Fact]
    public void Follower_AllGatedMetrics_InnerExporterNotCalled()
    {
        var leaderElection = new StubLeaderElection { IsLeader = false };
        var (inner, provider, _, leaderMeter) = CreateTestPipeline(leaderElection);

        // Record only on the gated (business/leader) meter -- nothing on pipeline meter.
        leaderMeter.CreateGauge<double>("snmp_gauge").Record(99.0);

        provider.ForceFlush();

        // When all metrics are gated the inner exporter must NOT be called (empty filtered batch).
        // MetricRoleGatedExporter returns ExportResult.Success for the all-gated case,
        // not ExportResult.Failure (which would trigger OTel SDK retry logic).
        Assert.Equal(0, inner.ExportCallCount);
    }

    // -----------------------------------------------------------------------
    // 4. ParentProvider reflection breakage detection
    //    Fails immediately if a future OTel SDK upgrade removes/renames/restructures ParentProvider.
    // -----------------------------------------------------------------------

    [Fact]
    public void ParentProviderProperty_IsAccessibleViaReflection()
    {
        var prop = typeof(BaseExporter<Metric>)
            .GetProperty("ParentProvider", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(prop);
        Assert.True(prop!.CanRead, "ParentProvider must have a readable getter");
        Assert.True(prop.CanWrite, "ParentProvider must be writable via reflection (internal set)");
        // Confirm the setter is NOT publicly accessible: the whole point is that MetricRoleGatedExporter
        // must use reflection to reach it. If the setter becomes public this test should be updated
        // because reflection is no longer necessary.
        Assert.False(prop.SetMethod?.IsPublic ?? true,
            "ParentProvider setter should remain internal -- if this fails, reflection is no longer needed");
    }

    // -----------------------------------------------------------------------
    // 5. Null guard tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_NullInner_Throws()
    {
        var leaderElection = new StubLeaderElection();
        Assert.Throws<ArgumentNullException>(() =>
            new MetricRoleGatedExporter(null!, new Lazy<ILeaderElection>(() => leaderElection), TelemetryConstants.LeaderMeterName));
    }

    [Fact]
    public void Constructor_NullLeaderElection_Throws()
    {
        var inner = new CapturingExporter();
        Assert.Throws<ArgumentNullException>(() =>
            new MetricRoleGatedExporter(inner, null!, TelemetryConstants.LeaderMeterName));
    }

    [Fact]
    public void Constructor_NullGatedMeterName_Throws()
    {
        var inner = new CapturingExporter();
        var leaderElection = new StubLeaderElection();
        Assert.Throws<ArgumentNullException>(() =>
            new MetricRoleGatedExporter(inner, new Lazy<ILeaderElection>(() => leaderElection), null!));
    }
}
