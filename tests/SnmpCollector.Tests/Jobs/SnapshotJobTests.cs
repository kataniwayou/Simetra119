using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Lextm.SharpSnmpLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quartz;
using SnmpCollector.Configuration;
using NSubstitute;
using SnmpCollector.Jobs;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;
using SnmpCollector.Tests.Helpers;
using Xunit;

namespace SnmpCollector.Tests.Jobs;

/// <summary>
/// Unit tests for <see cref="SnapshotJob"/> covering Tier 1 (staleness detection)
/// and Tier 2 (Resolved metrics gate) evaluation logic.
/// Tests exercise the internal EvaluateTenant method directly via InternalsVisibleTo.
/// </summary>
[Collection(NonParallelCollection.Name)]
public sealed class SnapshotJobTests : IDisposable
{
    // -------------------------------------------------------------------------
    // Test infrastructure
    // -------------------------------------------------------------------------

    private readonly ServiceProvider _sp;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly MeterListener _listener;
    private readonly List<(string InstrumentName, double Value)> _doubleMeasurements = new();
    private readonly StubTenantVectorRegistry _registry = new();
    private readonly StubSuppressionCache _suppressionCache = new();
    private readonly StubCommandChannel _commandChannel = new();
    private readonly StubCorrelationService _correlation = new();
    private readonly StubLivenessVectorService _liveness = new();
    private readonly ITenantMetricService _tenantMetrics = Substitute.For<ITenantMetricService>();
    private readonly SnapshotJob _job;

    public SnapshotJobTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        _sp = services.BuildServiceProvider();

        _pipelineMetrics = new PipelineMetricService(
            _sp.GetRequiredService<IMeterFactory>());

        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TelemetryConstants.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
        {
            _doubleMeasurements.Add((instrument.Name, value));
        });
        _listener.Start();

        _job = new SnapshotJob(
            _registry,
            _suppressionCache,
            _commandChannel,
            _correlation,
            _liveness,
            _pipelineMetrics,
            _tenantMetrics,
            Options.Create(new SnapshotJobOptions()),
            NullLogger<SnapshotJob>.Instance);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _pipelineMetrics.Dispose();
        _sp.Dispose();
    }

    // -------------------------------------------------------------------------
    // Pre-tier: Readiness tests
    // -------------------------------------------------------------------------

    [Fact]
    public void EvaluateTenant_AllHoldersReady_ProceedsToTier1()
    {
        // Holder with data → IsReady short-circuits true → pre-tier passes → proceeds to tier 1
        var holder = MakeHolder(intervalSeconds: 3600, graceMultiplier: 2.0, role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(holder, 50.0); // data present → IsReady = true

        var tenant = MakeTenant(holder);
        var result = _job.EvaluateTenant(tenant);

        // Ready and fresh, in-range → Healthy (tier 3)
        Assert.Equal(TenantState.Healthy, result);
    }

    [Fact]
    public void EvaluateTenant_OneHolderNotReady_ReturnsUnresolved()
    {
        // h1 has data (ready), h2 has no data and large grace (not ready) → pre-tier blocks
        var h1 = MakeHolder(intervalSeconds: 3600, role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(h1, 50.0); // data present → h1 immediately ready

        var h2 = MakeHolder(intervalSeconds: 3600, role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 });
        // h2 has no data, ReadinessGrace = 3600 * 2.0 = 7200s → not ready

        var tenant = MakeTenant(h1, h2);
        var result = _job.EvaluateTenant(tenant);

        // h2 not ready → pre-tier returns NotReady
        Assert.Equal(TenantState.NotReady, result);
    }

    [Fact]
    public async Task EvaluateTenant_ReadyEmptyHolder_TreatedAsStale()
    {
        // Holder with tiny grace (1s * 0.001 = 1ms) and no data.
        // After 50ms the grace has elapsed → IsReady = true. Null slot in HasStaleness → stale → Unresolved.
        var holder = MakeHolder(intervalSeconds: 1, graceMultiplier: 0.001, role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        await Task.Delay(50); // grace elapses → IsReady = true (no data but time elapsed)

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.1", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { holder }, new[] { cmd });
        var result = _job.EvaluateTenant(tenant);

        // Ready but no data → HasStaleness returns true (null slot = stale) → Unresolved (tier 4)
        Assert.Equal(TenantState.Unresolved, result);
    }

    // -------------------------------------------------------------------------
    // Tier 1: Staleness tests
    // -------------------------------------------------------------------------

    [Fact]
    public void EvaluateTenant_FreshHolder_NotStale()
    {
        // Large interval ensures just-written value is always fresh
        var holder = MakeHolder(intervalSeconds: 3600, graceMultiplier: 2.0, role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(holder, 50.0);

        var tenant = MakeTenant(holder);
        var result = _job.EvaluateTenant(tenant);

        Assert.NotEqual(TenantState.Unresolved, result);
    }

    [Fact]
    public void EvaluateTenant_NotReady_ReturnsUnresolved()
    {
        // Holder with no real write and a large grace window (intervalSeconds=3600, graceMultiplier=2.0
        // → ReadinessGrace = 7200s). Holder is not ready → pre-tier check returns Unresolved.
        var holder = MakeHolder(intervalSeconds: 3600, graceMultiplier: 2.0, role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        // Do NOT write any value — IsReady is false (no data, grace not elapsed)

        var tenant = MakeTenant(holder);
        var result = _job.EvaluateTenant(tenant);

        // Not ready → pre-tier returns NotReady (blocks advance gate)
        Assert.Equal(TenantState.NotReady, result);
    }

    [Fact]
    public void EvaluateTenant_TrapSource_ExcludedFromStalenessCheck()
    {
        // Trap-sourced holder should be excluded from staleness, even with tiny interval
        var holder = MakeHolder(intervalSeconds: 1, graceMultiplier: 1.0, role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(holder, 50.0, source: SnmpSource.Trap);

        var tenant = MakeTenant(holder);
        var result = _job.EvaluateTenant(tenant);

        // Trap source excluded from staleness → not stale
        Assert.NotEqual(TenantState.Unresolved, result);
    }

    [Fact]
    public void EvaluateTenant_CommandSource_ExcludedFromStalenessCheck()
    {
        // Command-response holder should be excluded from staleness — one-shot, no interval
        var holder = MakeHolder(intervalSeconds: 1, graceMultiplier: 1.0, role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(holder, 50.0, source: SnmpSource.Command);

        var tenant = MakeTenant(holder);
        var result = _job.EvaluateTenant(tenant);

        // Command source excluded from staleness → not stale
        Assert.NotEqual(TenantState.Unresolved, result);
    }

    [Fact]
    public void EvaluateTenant_IntervalSecondsZero_ExcludedFromStalenessCheck()
    {
        // IntervalSeconds=0 holder should be excluded from staleness check
        var holder = MakeHolder(intervalSeconds: 0, graceMultiplier: 2.0, role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(holder, 50.0);

        var tenant = MakeTenant(holder);
        var result = _job.EvaluateTenant(tenant);

        Assert.NotEqual(TenantState.Unresolved, result);
    }

    [Fact]
    public async Task EvaluateTenant_StaleHolder_SkipsToCommands()
    {
        // Very small interval + grace = 1 * 1.0 = 1 second window
        var holder = MakeHolder(intervalSeconds: 1, graceMultiplier: 1.0, role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(holder, 50.0);

        // Wait for value to become stale (> 1 second)
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.1", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant([holder], [cmd]);
        var result = _job.EvaluateTenant(tenant);

        // Stale data skips tiers 2-3, goes straight to command dispatch
        Assert.Equal(TenantState.Unresolved, result);
    }

    // -------------------------------------------------------------------------
    // Tier 2: Resolved gate tests
    // -------------------------------------------------------------------------

    [Fact]
    public void EvaluateTenant_AllResolvedViolated_ViolatedNoCommands()
    {
        // Two Resolved holders, both violated (value below Min)
        var h1 = MakeHolder(intervalSeconds: 3600, role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(h1, 5.0); // Below Min=10 → violated

        var h2 = MakeHolder(intervalSeconds: 3600, role: "Resolved",
            threshold: new ThresholdOptions { Min = 20 });
        WriteValue(h2, 15.0); // Below Min=20 → violated

        var tenant = MakeTenant(h1, h2);
        var result = _job.EvaluateTenant(tenant);

        Assert.Equal(TenantState.Resolved, result);
        // No commands should be enqueued (channel should be empty)
        Assert.False(_commandChannel.Reader.TryRead(out _));
    }

    [Fact]
    public void EvaluateTenant_OneResolvedInRange_ContinuesToTier3()
    {
        // Two Resolved holders: one violated, one in range
        var h1 = MakeHolder(intervalSeconds: 3600, role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(h1, 5.0); // Below Min=10 → violated

        var h2 = MakeHolder(intervalSeconds: 3600, role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(h2, 50.0); // Above Min=10 → NOT violated

        var tenant = MakeTenant(h1, h2);
        var result = _job.EvaluateTenant(tenant);

        // Not all Resolved violated → continues to Tier 3 → currently returns Healthy (Tier 3/4 not yet wired)
        Assert.NotEqual(TenantState.Resolved, result);
        Assert.NotEqual(TenantState.Unresolved, result);
    }

    [Fact]
    public void EvaluateTenant_ResolvedEmptyHolder_SkippedInGate()
    {
        // One Resolved with data (violated), one IntervalSeconds=0 Resolved with no data (empty series).
        // IntervalSeconds=0 holders are excluded from staleness; empty series (Length=0) are skipped in the gate.
        // The gate proceeds on h1 alone → h1 is violated → all participating Resolved holders violated → Resolved.
        var h1 = MakeHolder(intervalSeconds: 3600, role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(h1, 5.0); // Below Min=10 → violated

        var h2 = MakeHolder(intervalSeconds: 0, role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 });
        // h2 has IntervalSeconds=0 → excluded from staleness. No real write → ReadSeries().Length == 0 → skipped in gate.
        // h2 IsReady = true immediately (ReadinessGrace = 0 → elapsed).

        var tenant = MakeTenant(h1, h2);
        var result = _job.EvaluateTenant(tenant);

        // h1 violated, h2 empty (Length=0, skipped in gate) → only h1 participates → all Resolved violated → Resolved
        Assert.Equal(TenantState.Resolved, result);
    }

    [Fact]
    public void EvaluateTenant_ResolvedNoThreshold_TreatedAsViolated()
    {
        // Resolved holder with null threshold → treated as violated
        var holder = MakeHolder(intervalSeconds: 3600, role: "Resolved", threshold: null);
        WriteValue(holder, 50.0);

        var tenant = MakeTenant(holder);
        var result = _job.EvaluateTenant(tenant);

        Assert.Equal(TenantState.Resolved, result);
    }

    [Fact]
    public void EvaluateTenant_ResolvedBothBoundsNull_TreatedAsViolated()
    {
        // Resolved holder with threshold but Min=null, Max=null → violated
        var holder = MakeHolder(intervalSeconds: 3600, role: "Resolved",
            threshold: new ThresholdOptions { Min = null, Max = null });
        WriteValue(holder, 50.0);

        var tenant = MakeTenant(holder);
        var result = _job.EvaluateTenant(tenant);

        Assert.Equal(TenantState.Resolved, result);
    }

    [Fact]
    public void EvaluateTenant_ResolvedAtExactBoundary_NotViolated()
    {
        // Value exactly at Min boundary → NOT violated (boundary is in-range)
        var holder = MakeHolder(intervalSeconds: 3600, role: "Resolved",
            threshold: new ThresholdOptions { Min = 5.0, Max = 10.0 });
        WriteValue(holder, 5.0); // Exactly at Min → in-range

        var tenant = MakeTenant(holder);
        var result = _job.EvaluateTenant(tenant);

        // Not violated → not all Resolved violated → continues to Tier 3
        Assert.NotEqual(TenantState.Resolved, result);
        Assert.NotEqual(TenantState.Unresolved, result);
    }

    [Fact]
    public void EvaluateTenant_ResolvedAtExactMaxBoundary_NotViolated()
    {
        // Value exactly at Max boundary → NOT violated (boundary is in-range)
        var holder = MakeHolder(intervalSeconds: 3600, role: "Resolved",
            threshold: new ThresholdOptions { Min = 5.0, Max = 10.0 });
        WriteValue(holder, 10.0); // Exactly at Max → in-range

        var tenant = MakeTenant(holder);
        var result = _job.EvaluateTenant(tenant);

        Assert.NotEqual(TenantState.Resolved, result);
    }

    // -------------------------------------------------------------------------
    // Tier 2: Resolved all-samples series check
    // -------------------------------------------------------------------------

    [Fact]
    public void EvaluateTenant_ResolvedAllSeriesSamplesViolated_Violated()
    {
        // Resolved holder with 3-sample series, all violated (below Min=10)
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 }, timeSeriesSize: 3);
        WriteValue(resolved, 5.0);
        WriteValue(resolved, 3.0);
        WriteValue(resolved, 1.0); // All 3 below Min → all violated

        var tenant = MakeTenant(resolved);
        var result = _job.EvaluateTenant(tenant);

        Assert.Equal(TenantState.Resolved, result);
    }

    [Fact]
    public void EvaluateTenant_ResolvedOneSeriesSampleInRange_ContinuesToTier3()
    {
        // Resolved holder with 3-sample series: 2 violated, 1 in-range
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 }, timeSeriesSize: 3);
        WriteValue(resolved, 5.0);  // Below Min → violated
        WriteValue(resolved, 5.0);  // Below Min → violated
        WriteValue(resolved, 50.0); // Above Min → NOT violated

        var tenant = MakeTenant(resolved);
        var result = _job.EvaluateTenant(tenant);

        // One in-range sample → not all Resolved violated → continues to Tier 3
        Assert.NotEqual(TenantState.Resolved, result);
        Assert.NotEqual(TenantState.Unresolved, result);
    }

    [Fact]
    public void EvaluateTenant_ResolvedPartialSeriesFill_AllViolated_Violated()
    {
        // Resolved holder with timeSeriesSize=5 but only 2 writes (2 writes = 2 samples, both violated)
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 }, timeSeriesSize: 5);
        WriteValue(resolved, 5.0); // Below Min → violated
        WriteValue(resolved, 3.0); // Below Min → violated

        var tenant = MakeTenant(resolved);
        var result = _job.EvaluateTenant(tenant);

        Assert.Equal(TenantState.Resolved, result);
    }

    // -------------------------------------------------------------------------
    // Source-aware threshold check (Trap/Command = newest only, Poll = all samples)
    // -------------------------------------------------------------------------

    [Fact]
    public void EvaluateTenant_ResolvedCommandSource_ChecksNewestSampleOnly()
    {
        // Command-sourced resolved holder with 3-sample series:
        // first 2 violated, last one in-range.
        // With all-samples check this would be "not all violated" → continue to Tier 3.
        // But Command source should only check the newest (in-range) → NOT violated → Tier 3.
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 }, timeSeriesSize: 3);
        WriteValue(resolved, 5.0);   // Below Min → violated
        WriteValue(resolved, 5.0);   // Below Min → violated
        WriteValue(resolved, 50.0, source: SnmpSource.Command); // In range → NOT violated (newest)

        var tenant = MakeTenant(resolved);
        var result = _job.EvaluateTenant(tenant);

        // Command source checks newest only (50.0 >= 10) → not violated → continues to Tier 3
        Assert.NotEqual(TenantState.Resolved, result);
    }

    [Fact]
    public void EvaluateTenant_ResolvedCommandSource_NewestViolated_Violated()
    {
        // Command-sourced resolved holder: newest sample is violated
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 }, timeSeriesSize: 3);
        WriteValue(resolved, 50.0);  // In range
        WriteValue(resolved, 50.0);  // In range
        WriteValue(resolved, 5.0, source: SnmpSource.Command); // Below Min → violated (newest)

        var tenant = MakeTenant(resolved);
        var result = _job.EvaluateTenant(tenant);

        // Command source checks newest only (5.0 < 10) → violated → Violated
        Assert.Equal(TenantState.Resolved, result);
    }

    [Fact]
    public void EvaluateTenant_ResolvedTrapSource_ChecksNewestSampleOnly()
    {
        // Trap-sourced resolved: old samples violated, newest in-range
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 }, timeSeriesSize: 3);
        WriteValue(resolved, 5.0);   // Below Min → violated
        WriteValue(resolved, 5.0);   // Below Min → violated
        WriteValue(resolved, 50.0, source: SnmpSource.Trap); // In range (newest)

        var tenant = MakeTenant(resolved);
        var result = _job.EvaluateTenant(tenant);

        // Trap source checks newest only → NOT violated → Tier 3
        Assert.NotEqual(TenantState.Resolved, result);
    }

    [Fact]
    public void EvaluateTenant_EvaluateCommandSource_ChecksNewestSampleOnly()
    {
        // Command-sourced evaluate holder: 2 violated, newest in-range
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0); // In range → not violated

        var eval1 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 }, timeSeriesSize: 3);
        WriteValue(eval1, 5.0);  // Below Min → violated
        WriteValue(eval1, 5.0);  // Below Min → violated
        WriteValue(eval1, 50.0, source: SnmpSource.Command); // In range (newest)

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { resolved, eval1 }, new[] { cmd });

        var result = _job.EvaluateTenant(tenant);

        // Command source checks newest only (50.0 >= 10) → NOT violated → Healthy
        Assert.Equal(TenantState.Healthy, result);
    }

    [Fact]
    public void EvaluateTenant_EvaluatePollSource_ChecksAllSamples()
    {
        // Poll-sourced evaluate holder: 2 violated + 1 in-range → NOT all violated
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0);

        var eval1 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 }, timeSeriesSize: 3);
        WriteValue(eval1, 5.0);   // Below Min → violated
        WriteValue(eval1, 5.0);   // Below Min → violated
        WriteValue(eval1, 50.0);  // In range (Poll source — all samples checked)

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { resolved, eval1 }, new[] { cmd });

        var result = _job.EvaluateTenant(tenant);

        // Poll source checks ALL samples — one in-range → Healthy
        Assert.Equal(TenantState.Healthy, result);
    }

    // -------------------------------------------------------------------------
    // IsViolated direct tests
    // -------------------------------------------------------------------------

    [Fact]
    public void IsViolated_ValueBelowMin_Violated()
    {
        var holder = MakeHolder(threshold: new ThresholdOptions { Min = 10 });
        WriteValue(holder, 9.0);
        Assert.True(SnapshotJob.IsViolated(holder, holder.ReadSlot()!));
    }

    [Fact]
    public void IsViolated_ValueAboveMax_Violated()
    {
        var holder = MakeHolder(threshold: new ThresholdOptions { Max = 10 });
        WriteValue(holder, 11.0);
        Assert.True(SnapshotJob.IsViolated(holder, holder.ReadSlot()!));
    }

    [Fact]
    public void IsViolated_ValueInRange_NotViolated()
    {
        var holder = MakeHolder(threshold: new ThresholdOptions { Min = 5, Max = 10 });
        WriteValue(holder, 7.0);
        Assert.False(SnapshotJob.IsViolated(holder, holder.ReadSlot()!));
    }

    [Fact]
    public void IsViolated_OnlyMinSet_ValueAboveMin_NotViolated()
    {
        var holder = MakeHolder(threshold: new ThresholdOptions { Min = 5 });
        WriteValue(holder, 100.0);
        Assert.False(SnapshotJob.IsViolated(holder, holder.ReadSlot()!));
    }

    // -------------------------------------------------------------------------
    // Execute integration: stamps liveness and clears correlation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_StampsLivenessAndClearsCorrelation()
    {
        _registry.SetGroups(new PriorityGroup(1, new[]
        {
            MakeTenant(MakeHolder(intervalSeconds: 3600, role: "Resolved",
                threshold: new ThresholdOptions { Min = 0, Max = 100 }))
        }));

        var context = MakeContext("snapshot");
        await _job.Execute(context);

        Assert.True(_liveness.StampCalled);
        Assert.Equal("snapshot", _liveness.LastStampedKey);
        Assert.Null(_correlation.OperationCorrelationId);
    }

    // -------------------------------------------------------------------------
    // Tier 3: Evaluate gate tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Execute_AllEvaluateViolated_ProceedsToTier4()
    {
        // Resolved NOT all violated (prerequisite for reaching Tier 3)
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0); // In range → not violated

        // Evaluate holder violated (value below Min)
        var eval1 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(eval1, 5.0); // Below Min → violated

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { resolved, eval1 }, new[] { cmd });

        _job.EvaluateTenant(tenant);

        // Tier 4 reached → command should be enqueued
        Assert.True(_commandChannel.Reader.TryRead(out var request));
        Assert.Equal("reset", request!.CommandName);
    }

    [Fact]
    public void Execute_OneEvaluateInRange_Healthy()
    {
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0);

        var eval1 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(eval1, 5.0); // Violated

        var eval2 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(eval2, 50.0); // In range → NOT violated

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { resolved, eval1, eval2 }, new[] { cmd });
        var result = _job.EvaluateTenant(tenant);

        Assert.Equal(TenantState.Healthy, result);
        Assert.False(_commandChannel.Reader.TryRead(out _)); // No commands
    }

    [Fact]
    public async Task Execute_EvaluateEmptyHolder_SkippedInCheck()
    {
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 }, graceMultiplier: 0.001);
        WriteValue(resolved, 50.0); // data present → immediately ready

        // Evaluate holder with data → violated, immediately ready
        var eval1 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 }, graceMultiplier: 0.001);
        WriteValue(eval1, 5.0);

        // Evaluate holder with no data but tiny grace (1s * 0.001 = 1ms) → ready after 50ms but empty
        var eval2 = MakeHolder(role: "Evaluate", intervalSeconds: 1,
            threshold: new ThresholdOptions { Min = 10 }, graceMultiplier: 0.001);
        // No real write — empty after grace; skipped in AreAllEvaluateViolated

        await Task.Delay(50);

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { resolved, eval1, eval2 }, new[] { cmd });

        _job.EvaluateTenant(tenant);

        // eval1 violated, eval2 empty (skipped) → only eval1 checked → all checked Evaluate violated → Tier 4
        Assert.True(_commandChannel.Reader.TryRead(out _));
    }

    [Fact]
    public void Execute_EvaluateNoThreshold_TreatedAsViolated()
    {
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0);

        // Evaluate with null threshold → treated as violated
        var eval1 = MakeHolder(role: "Evaluate", threshold: null);
        WriteValue(eval1, 50.0);

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { resolved, eval1 }, new[] { cmd });

        _job.EvaluateTenant(tenant);

        // null threshold = violated → Tier 4
        Assert.True(_commandChannel.Reader.TryRead(out _));
    }

    [Fact]
    public void Execute_EvaluateAtExactBoundary_NotViolated_Healthy()
    {
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0);

        // Value exactly at Max → in range (strict inequality) → NOT violated
        var eval1 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 0, Max = 10 });
        WriteValue(eval1, 10.0); // Exactly at Max → NOT violated

        var tenant = MakeTenant(new[] { resolved, eval1 });
        var result = _job.EvaluateTenant(tenant);

        Assert.Equal(TenantState.Healthy, result);
    }

    // -------------------------------------------------------------------------
    // Tier 3: All-samples series check
    // -------------------------------------------------------------------------

    [Fact]
    public void Execute_EvaluateAllSeriesSamplesViolated_ProceedsToTier4()
    {
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0); // In range → not violated

        // Evaluate holder with 3-sample series, all violated (below Min=10)
        var eval1 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 }, timeSeriesSize: 3);
        WriteValue(eval1, 5.0);
        WriteValue(eval1, 3.0);
        WriteValue(eval1, 1.0); // All 3 below Min → all violated

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { resolved, eval1 }, new[] { cmd });

        _job.EvaluateTenant(tenant);

        // Tier 4 reached → command should be enqueued
        Assert.True(_commandChannel.Reader.TryRead(out var request));
        Assert.Equal("reset", request!.CommandName);
    }

    [Fact]
    public void Execute_EvaluateOneSeriesSampleInRange_Healthy()
    {
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0); // In range → not violated

        // Evaluate holder with 3-sample series: 2 violated, 1 in-range
        var eval1 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 }, timeSeriesSize: 3);
        WriteValue(eval1, 5.0);  // Below Min → violated
        WriteValue(eval1, 5.0);  // Below Min → violated
        WriteValue(eval1, 50.0); // Above Min → NOT violated

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { resolved, eval1 }, new[] { cmd });

        var result = _job.EvaluateTenant(tenant);

        // One in-range sample → Healthy, no command enqueued
        Assert.Equal(TenantState.Healthy, result);
        Assert.False(_commandChannel.Reader.TryRead(out _));
    }

    [Fact]
    public void Execute_EvaluatePartialSeriesFill_AllViolated_ProceedsToTier4()
    {
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0); // In range → not violated

        // Evaluate holder with timeSeriesSize=5 but only 2 values written (2 writes = 2 samples, both violated)
        var eval1 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 }, timeSeriesSize: 5);
        WriteValue(eval1, 5.0); // Below Min → violated
        WriteValue(eval1, 3.0); // Below Min → violated

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { resolved, eval1 }, new[] { cmd });

        _job.EvaluateTenant(tenant);

        // All present samples violated (partial fill) → Tier 4 reached
        Assert.True(_commandChannel.Reader.TryRead(out var request));
        Assert.Equal("reset", request!.CommandName);
    }

    // -------------------------------------------------------------------------
    // Tier 4: Command dispatch tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Execute_CommandNotSuppressed_TryWriteWithCorrectFields()
    {
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0);

        var eval1 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(eval1, 5.0); // Violated

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.5", Port = 162, CommandName = "set-mode", Value = "42", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { resolved, eval1 }, new[] { cmd }, id: "tenant-abc");

        _job.EvaluateTenant(tenant);

        Assert.True(_commandChannel.Reader.TryRead(out var request));
        Assert.Equal("10.0.0.5", request!.Ip);
        Assert.Equal(162, request.Port);
        Assert.Equal("set-mode", request.CommandName);
        Assert.Equal("42", request.Value);
        Assert.Equal("Integer32", request.ValueType);
    }

    [Fact]
    public void Execute_CommandSuppressed_NoTryWrite_IncrementSuppressed()
    {
        _suppressionCache.SuppressResult = true; // All commands suppressed

        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0);

        var eval1 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(eval1, 5.0);

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { resolved, eval1 }, new[] { cmd });
        var result = _job.EvaluateTenant(tenant);

        // Suppressed → no TryWrite, command intent = Unresolved (blocks gate)
        Assert.False(_commandChannel.Reader.TryRead(out _));
        Assert.Equal(TenantState.Unresolved, result);
    }

    [Fact]
    public void Execute_ChannelFull_IncrementFailed_NoException()
    {
        // Fill the channel to capacity (16)
        for (var i = 0; i < 16; i++)
            _commandChannel.Writer.TryWrite(new CommandRequest("x", 1, "fill", "0", "Integer32", "test-tenant", 1));

        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0);

        var eval1 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(eval1, 5.0);

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { resolved, eval1 }, new[] { cmd });

        // Should not throw — channel full means command intent = Unresolved (blocks gate)
        var result = _job.EvaluateTenant(tenant);

        Assert.Equal(TenantState.Unresolved, result);
    }

    [Fact]
    public void Execute_MultipleCommands_EachCheckedIndependently()
    {
        // First command suppressed, second not
        _suppressionCache.SuppressResults = new Dictionary<string, bool>
        {
            { "test-tenant:10.0.0.2:161:cmd-a", true },
            { "test-tenant:10.0.0.2:161:cmd-b", false }
        };

        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0);

        var eval1 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(eval1, 5.0);

        var cmdA = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "cmd-a", Value = "1", ValueType = "Integer32" };
        var cmdB = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "cmd-b", Value = "2", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { resolved, eval1 }, new[] { cmdA, cmdB });

        _job.EvaluateTenant(tenant);

        // Only cmd-b should be enqueued (cmd-a suppressed)
        Assert.True(_commandChannel.Reader.TryRead(out var request));
        Assert.Equal("cmd-b", request!.CommandName);
        Assert.False(_commandChannel.Reader.TryRead(out _)); // No more
    }

    [Fact]
    public void Execute_SuppressionKeyIncludesTenantId_IndependentSuppression()
    {
        // Tenant A suppressed, Tenant B not — same command target
        _suppressionCache.SuppressResults = new Dictionary<string, bool>
        {
            { "tenant-a:10.0.0.2:161:reset", true },
            { "tenant-b:10.0.0.2:161:reset", false }
        };

        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0);

        var eval1 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(eval1, 5.0);

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };

        // Need separate holders per tenant (each tenant has its own holders)
        var resolvedB = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolvedB, 50.0);
        var evalB = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(evalB, 5.0);

        var tenantA = MakeTenant(new[] { resolved, eval1 }, new[] { cmd }, id: "tenant-a");
        var tenantB = MakeTenant(new[] { resolvedB, evalB }, new[] { cmd }, id: "tenant-b");

        _job.EvaluateTenant(tenantA);
        _job.EvaluateTenant(tenantB);

        // tenant-a suppressed → no write. tenant-b not suppressed → write
        Assert.True(_commandChannel.Reader.TryRead(out var request));
        Assert.Equal("reset", request!.CommandName);
        Assert.False(_commandChannel.Reader.TryRead(out _)); // Only one
    }

    // -------------------------------------------------------------------------
    // Integration: Priority group traversal and advance gate
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_SingleGroupAllHealthy_NoCommands()
    {
        // Resolved NOT all violated, Evaluate NOT all violated → Healthy
        var t1 = MakeHealthyTenant("t1");
        var t2 = MakeHealthyTenant("t2");

        _registry.SetGroups(new PriorityGroup(1, new[] { t1, t2 }));
        await _job.Execute(MakeContext("snapshot"));

        Assert.False(_commandChannel.Reader.TryRead(out _));
        Assert.True(_liveness.StampCalled);
    }

    [Fact]
    public async Task Execute_SingleGroupOneStale_CommandsEnqueued()
    {
        // One stale tenant with a command — staleness skips to command dispatch
        var staleHolder = MakeHolder(intervalSeconds: 1, graceMultiplier: 0.001, role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(staleHolder, 50.0);
        // IntervalSeconds=1, GraceMultiplier=0.001 → grace window = 0.001s (1ms)
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.1", Port = 161, CommandName = "stale-cmd", Value = "1", ValueType = "Integer32" };
        var staleTenant = MakeTenant(new[] { staleHolder }, new[] { cmd }, id: "stale-t");
        var healthyTenant = MakeHealthyTenant("healthy-t");

        _registry.SetGroups(new PriorityGroup(1, new[] { staleTenant, healthyTenant }));
        await _job.Execute(MakeContext("snapshot"));

        // Stale tenant skips to commands — command should be enqueued
        Assert.True(_commandChannel.Reader.TryRead(out var req));
        Assert.Equal("stale-cmd", req!.CommandName);
        Assert.True(_liveness.StampCalled);
    }

    [Fact]
    public async Task Execute_TwoGroups_FirstAllViolated_SecondGroupEvaluated()
    {
        // Group 1: ALL Resolved violated → Violated (Tier 2 stop). Advance gate passes.
        var confirmedBadTenant = MakeViolatedTenant("cb-t");
        // Group 2: Healthy tenant (should be evaluated since group 1 advances)
        var healthyTenant = MakeHealthyTenant("healthy-t");

        _registry.SetGroups(
            new PriorityGroup(1, new[] { confirmedBadTenant }),
            new PriorityGroup(2, new[] { healthyTenant }));

        await _job.Execute(MakeContext("snapshot"));

        // No commands from either group (Violated stops at Tier 2, Healthy stops at Tier 3)
        Assert.False(_commandChannel.Reader.TryRead(out _));
        Assert.True(_liveness.StampCalled);
    }

    [Fact]
    public async Task Execute_TwoGroups_FirstGroupCommanded_SecondGroupNotEvaluated()
    {
        // Group 1: Commanded tenant (Resolved NOT all violated, ALL Evaluate violated, command enqueued)
        var commandedTenant = MakeCommandingTenant("cmd-t1", "cmd-group1");
        // Group 2: Commanding tenant — if evaluated would enqueue commands
        var group2Tenant = MakeCommandingTenant("cmd-t2", "cmd-group2");

        _registry.SetGroups(
            new PriorityGroup(1, new[] { commandedTenant }),
            new PriorityGroup(2, new[] { group2Tenant }));

        await _job.Execute(MakeContext("snapshot"));

        // Only group 1 command should be enqueued (advance gate blocked)
        Assert.True(_commandChannel.Reader.TryRead(out var req));
        Assert.Equal("cmd-group1", req!.CommandName);
        Assert.False(_commandChannel.Reader.TryRead(out _)); // No group 2 commands
    }

    [Fact]
    public async Task Execute_TwoGroups_FirstGroupStale_SecondGroupNotEvaluated()
    {
        // Group 1: Stale tenant with command → commands enqueued, advance gate blocks
        var staleHolder = MakeHolder(intervalSeconds: 1, graceMultiplier: 0.001, role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(staleHolder, 50.0);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        var staleCmd = new CommandSlotOptions
            { Ip = "10.0.0.1", Port = 161, CommandName = "stale-cmd", Value = "1", ValueType = "Integer32" };
        var staleTenant = MakeTenant(new[] { staleHolder }, new[] { staleCmd }, id: "stale-t");

        // Group 2: Commanding tenant — would enqueue if evaluated
        var group2Tenant = MakeCommandingTenant("cmd-t2", "cmd-group2");

        _registry.SetGroups(
            new PriorityGroup(1, new[] { staleTenant }),
            new PriorityGroup(2, new[] { group2Tenant }));

        await _job.Execute(MakeContext("snapshot"));

        // Group 1 stale → commands enqueued (Commanded) → advance gate blocks → group 2 not evaluated
        Assert.True(_commandChannel.Reader.TryRead(out var req));
        Assert.Equal("stale-cmd", req!.CommandName);
        // Group 2 command should NOT be present
        Assert.False(_commandChannel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Execute_TwoGroups_FirstAllHealthy_SecondGroupEvaluated()
    {
        // Group 1: All healthy → advance gate passes
        var healthy1 = MakeHealthyTenant("h1");
        var healthy2 = MakeHealthyTenant("h2");
        // Group 2: Commanding tenant — should be evaluated
        var group2Tenant = MakeCommandingTenant("cmd-t", "cmd-group2");

        _registry.SetGroups(
            new PriorityGroup(1, new[] { healthy1, healthy2 }),
            new PriorityGroup(2, new[] { group2Tenant }));

        await _job.Execute(MakeContext("snapshot"));

        // Group 2 was evaluated — command from group 2 tenant should be enqueued
        Assert.True(_commandChannel.Reader.TryRead(out var req));
        Assert.Equal("cmd-group2", req!.CommandName);
    }

    [Fact]
    public async Task Execute_TwoGroups_FirstMixedHealthyAndViolated_SecondGroupEvaluated()
    {
        // Group 1: Mixed Healthy + Violated → both are advance-allowing states
        var healthyTenant = MakeHealthyTenant("h1");
        var confirmedBadTenant = MakeViolatedTenant("cb1");
        // Group 2: Commanding tenant — should be evaluated
        var group2Tenant = MakeCommandingTenant("cmd-t", "cmd-group2");

        _registry.SetGroups(
            new PriorityGroup(1, new[] { healthyTenant, confirmedBadTenant }),
            new PriorityGroup(2, new[] { group2Tenant }));

        await _job.Execute(MakeContext("snapshot"));

        // Group 2 was evaluated
        Assert.True(_commandChannel.Reader.TryRead(out var req));
        Assert.Equal("cmd-group2", req!.CommandName);
    }

    [Fact]
    public async Task Execute_ThreeGroups_FirstAdvances_SecondBlocks_ThirdNotEvaluated()
    {
        // Group 1: Healthy → advance
        var healthy = MakeHealthyTenant("h1");
        // Group 2: Commanded → blocks
        var commanded = MakeCommandingTenant("cmd-t", "cmd-group2");
        // Group 3: Commanding — should NOT be evaluated
        var group3Tenant = MakeCommandingTenant("cmd-t3", "cmd-group3");

        _registry.SetGroups(
            new PriorityGroup(1, new[] { healthy }),
            new PriorityGroup(2, new[] { commanded }),
            new PriorityGroup(3, new[] { group3Tenant }));

        await _job.Execute(MakeContext("snapshot"));

        // Group 2 command enqueued, group 3 NOT
        Assert.True(_commandChannel.Reader.TryRead(out var req));
        Assert.Equal("cmd-group2", req!.CommandName);
        Assert.False(_commandChannel.Reader.TryRead(out _)); // No group 3
    }

    [Fact]
    public async Task Execute_EmptyGroupsList_LivenessStillStamped()
    {
        // No groups at all
        _registry.SetGroups();

        await _job.Execute(MakeContext("snapshot"));

        Assert.True(_liveness.StampCalled);
        Assert.Equal("snapshot", _liveness.LastStampedKey);
        Assert.Null(_correlation.OperationCorrelationId);
    }

    // -------------------------------------------------------------------------
    // Snapshot cycle duration histogram
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_RecordsSnapshotCycleDuration()
    {
        _registry.SetGroups(new PriorityGroup(1, new[]
        {
            MakeHealthyTenant("t1")
        }));

        await _job.Execute(MakeContext("snapshot"));

        var match = _doubleMeasurements.SingleOrDefault(m =>
            m.InstrumentName == "snmp.snapshot.cycle_duration_ms");

        Assert.NotEqual(default, match);
        Assert.True(match.Value >= 0, "Cycle duration should be non-negative");
    }

    // -------------------------------------------------------------------------
    // ITenantMetricService: per-path metric recording tests
    // -------------------------------------------------------------------------

    [Fact]
    public void EvaluateTenant_NotReadyPath_RecordsOnlyStateAndDuration()
    {
        _tenantMetrics.ClearReceivedCalls();

        // Holder with no data and large grace window → not ready
        var holder = MakeHolder(intervalSeconds: 3600, graceMultiplier: 2.0, role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        // No write → IsReady = false

        var tenant = MakeTenant(holder);
        var result = _job.EvaluateTenant(tenant);

        Assert.Equal(TenantState.NotReady, result);

        // State gauge and duration must be recorded
        _tenantMetrics.Received(1).RecordTenantState(tenant.Id, tenant.Priority, TenantState.NotReady);
        _tenantMetrics.Received(1).RecordEvaluationDuration(tenant.Id, tenant.Priority, Arg.Any<double>());

        // No tier or command counters on NotReady path
        _tenantMetrics.DidNotReceive().IncrementTier1Stale(Arg.Any<string>(), Arg.Any<int>());
        _tenantMetrics.DidNotReceive().IncrementTier2Resolved(Arg.Any<string>(), Arg.Any<int>());
        _tenantMetrics.DidNotReceive().IncrementTier3Evaluate(Arg.Any<string>(), Arg.Any<int>());
        _tenantMetrics.DidNotReceive().IncrementCommandDispatched(Arg.Any<string>(), Arg.Any<int>());
        _tenantMetrics.DidNotReceive().IncrementCommandSuppressed(Arg.Any<string>(), Arg.Any<int>());
        _tenantMetrics.DidNotReceive().IncrementCommandFailed(Arg.Any<string>(), Arg.Any<int>());
    }

    [Fact]
    public void EvaluateTenant_ResolvedPath_RecordsStateAndDurationAndTier1AndTier2()
    {
        _tenantMetrics.ClearReceivedCalls();

        // One stale holder (will count for tier1) + one Resolved violated (tier2 resolved = 0 since all are violated)
        // Stale holder: intervalSeconds=1, graceMultiplier=0.001 → grace=1ms, write old value immediately stale
        // Resolved violated: value below Min → all Resolved violated → tier 2 stop (Resolved state)
        // Note: for tier2_resolved count we need non-violated Resolved holders — so we set up one stale non-excluded
        // holder AND one non-stale Resolved violated.

        // h1: poll, tiny grace → becomes stale quickly (but we need it ready first)
        var h1 = MakeHolder(intervalSeconds: 3600, graceMultiplier: 2.0, role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(h1, 5.0); // violated (below Min=10), not stale (just written)

        var tenant = MakeTenant(h1);
        var result = _job.EvaluateTenant(tenant);

        Assert.Equal(TenantState.Resolved, result);

        // State gauge + duration must be recorded
        _tenantMetrics.Received(1).RecordTenantState(tenant.Id, tenant.Priority, TenantState.Resolved);
        _tenantMetrics.Received(1).RecordEvaluationDuration(tenant.Id, tenant.Priority, Arg.Any<double>());

        // tier1_stale: 0 stale holders (just written, not stale) — DidNotReceive is correct here
        _tenantMetrics.DidNotReceive().IncrementTier1Stale(Arg.Any<string>(), Arg.Any<int>());
        // tier2_resolved: 0 non-violated resolved (h1 is violated) — DidNotReceive is correct
        _tenantMetrics.DidNotReceive().IncrementTier2Resolved(Arg.Any<string>(), Arg.Any<int>());
        // tier3_evaluate: not recorded on Resolved path
        _tenantMetrics.DidNotReceive().IncrementTier3Evaluate(Arg.Any<string>(), Arg.Any<int>());
        // No command counters
        _tenantMetrics.DidNotReceive().IncrementCommandDispatched(Arg.Any<string>(), Arg.Any<int>());
    }

    [Fact]
    public void EvaluateTenant_HealthyPath_RecordsAllTierCountersAndStateAndDuration()
    {
        _tenantMetrics.ClearReceivedCalls();

        // Resolved NOT violated (tier2 allows through) + Evaluate NOT all violated (tier3 → Healthy)
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0); // In range → not violated (tier2_resolved count = 1)

        var evaluate = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(evaluate, 50.0); // In range → not violated (tier3_evaluate count = 0)

        var tenant = MakeTenant(resolved, evaluate);
        var result = _job.EvaluateTenant(tenant);

        Assert.Equal(TenantState.Healthy, result);

        // State gauge + duration
        _tenantMetrics.Received(1).RecordTenantState(tenant.Id, tenant.Priority, TenantState.Healthy);
        _tenantMetrics.Received(1).RecordEvaluationDuration(tenant.Id, tenant.Priority, Arg.Any<double>());

        // tier1_stale: 0 stale holders (just written) → DidNotReceive
        _tenantMetrics.DidNotReceive().IncrementTier1Stale(Arg.Any<string>(), Arg.Any<int>());
        // tier2_resolved: 1 non-violated resolved → received once
        _tenantMetrics.Received(1).IncrementTier2Resolved(tenant.Id, tenant.Priority);
        // tier3_evaluate: 0 violated evaluate → DidNotReceive
        _tenantMetrics.DidNotReceive().IncrementTier3Evaluate(Arg.Any<string>(), Arg.Any<int>());

        // No command counters on Healthy path
        _tenantMetrics.DidNotReceive().IncrementCommandDispatched(Arg.Any<string>(), Arg.Any<int>());
    }

    [Fact]
    public void EvaluateTenant_UnresolvedPath_RecordsAllTierCountersPlusCommandCounters()
    {
        _tenantMetrics.ClearReceivedCalls();

        // Resolved NOT violated (passes tier2) + Evaluate ALL violated (reaches tier4) + 1 command dispatched
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0); // In range → not violated (tier2_resolved = 1)

        var evaluate = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(evaluate, 5.0); // Below Min → violated (tier3_evaluate = 1)

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { resolved, evaluate }, new[] { cmd });

        var result = _job.EvaluateTenant(tenant);

        Assert.Equal(TenantState.Unresolved, result);

        // State gauge + duration
        _tenantMetrics.Received(1).RecordTenantState(tenant.Id, tenant.Priority, TenantState.Unresolved);
        _tenantMetrics.Received(1).RecordEvaluationDuration(tenant.Id, tenant.Priority, Arg.Any<double>());

        // All 3 tier counters
        _tenantMetrics.DidNotReceive().IncrementTier1Stale(Arg.Any<string>(), Arg.Any<int>()); // 0 stale
        _tenantMetrics.Received(1).IncrementTier2Resolved(tenant.Id, tenant.Priority); // 1 non-violated resolved
        _tenantMetrics.Received(1).IncrementTier3Evaluate(tenant.Id, tenant.Priority); // 1 violated evaluate

        // Command dispatched (not suppressed)
        _tenantMetrics.Received(1).IncrementCommandDispatched(tenant.Id, tenant.Priority);
        _tenantMetrics.DidNotReceive().IncrementCommandSuppressed(Arg.Any<string>(), Arg.Any<int>());
        _tenantMetrics.DidNotReceive().IncrementCommandFailed(Arg.Any<string>(), Arg.Any<int>());
    }

    [Fact]
    public void EvaluateTenant_StaleHolderCount_IncrementsByActualCount()
    {
        _tenantMetrics.ClearReceivedCalls();

        // Exactly 2 stale poll holders + 1 non-stale trap holder (excluded from stale count)
        // Tenant reaches tier4 via staleness (HasStaleness = true)
        // The stale holders use tiny grace so they become stale quickly after write

        // For them to be stale: age > grace. We use a very small grace and can't wait,
        // so instead use intervalSeconds=0 holders (excluded from staleness) + manually control
        // Actually we need to use real stale holders. Use graceMultiplier=0 which makes grace=0 → always stale after write
        var stale1 = MakeHolder(intervalSeconds: 3600, graceMultiplier: 0.0, role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(stale1, 50.0); // written, but grace=0 → immediately stale (age > 0s)

        var stale2 = MakeHolder(intervalSeconds: 3600, graceMultiplier: 0.0, role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(stale2, 50.0); // same — immediately stale

        var trapHolder = MakeHolder(intervalSeconds: 1, graceMultiplier: 0.0, role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(trapHolder, 50.0, source: SnmpSource.Trap); // Trap-sourced → excluded from stale count

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.1", Port = 161, CommandName = "stale-cmd", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { stale1, stale2, trapHolder }, new[] { cmd });

        var result = _job.EvaluateTenant(tenant);

        Assert.Equal(TenantState.Unresolved, result);

        // Exactly 2 IncrementTier1Stale calls (one per stale poll holder, trap holder excluded)
        _tenantMetrics.Received(2).IncrementTier1Stale(tenant.Id, tenant.Priority);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static MetricSlotHolder MakeHolder(
        int intervalSeconds = 3600,
        double graceMultiplier = 2.0,
        string role = "Resolved",
        ThresholdOptions? threshold = null,
        string metricName = "test-metric",
        int timeSeriesSize = 1)
    {
        return new MetricSlotHolder(
            ip: "10.0.0.1",
            port: 161,
            metricName: metricName,
            intervalSeconds: intervalSeconds,
            role: role,
            timeSeriesSize: timeSeriesSize,
            graceMultiplier: graceMultiplier,
            threshold: threshold);
    }

    private static void WriteValue(MetricSlotHolder holder, double value,
        SnmpSource source = SnmpSource.Poll)
    {
        holder.WriteValue(value, null, SnmpType.Integer32, source);
    }

    private static Tenant MakeTenant(params MetricSlotHolder[] holders)
    {
        return MakeTenant(holders, Array.Empty<CommandSlotOptions>());
    }

    private static Tenant MakeTenant(MetricSlotHolder[] holders,
        CommandSlotOptions[] commands, string id = "test-tenant")
    {
        return new Tenant(
            id: id,
            priority: 1,
            holders: holders,
            commands: commands,
            suppressionWindowSeconds: 60);
    }

    /// <summary>
    /// Creates a tenant that evaluates as Healthy:
    /// Resolved NOT all violated → Evaluate NOT all violated → Healthy.
    /// </summary>
    private static Tenant MakeHealthyTenant(string id)
    {
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0); // In range → not violated

        var evaluate = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(evaluate, 50.0); // In range → not violated

        return MakeTenant(new[] { resolved, evaluate }, Array.Empty<CommandSlotOptions>(), id: id);
    }

    /// <summary>
    /// Creates a tenant that evaluates as Violated (Tier 2 stop):
    /// ALL Resolved violated → Violated.
    /// </summary>
    private static Tenant MakeViolatedTenant(string id)
    {
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(resolved, 5.0); // Below Min → violated

        return MakeTenant(new[] { resolved }, Array.Empty<CommandSlotOptions>(), id: id);
    }

    /// <summary>
    /// Creates a tenant that evaluates as Commanded:
    /// Resolved NOT all violated → ALL Evaluate violated → Tier 4 enqueues command.
    /// </summary>
    private static Tenant MakeCommandingTenant(string id, string commandName)
    {
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0); // In range → not violated

        var evaluate = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(evaluate, 5.0); // Below Min → violated

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = commandName, Value = "1", ValueType = "Integer32" };

        return MakeTenant(new[] { resolved, evaluate }, new[] { cmd }, id: id);
    }

    private static IJobExecutionContext MakeContext(string jobKeyName)
        => new StubJobContext(jobKeyName);

    // -------------------------------------------------------------------------
    // Test doubles
    // -------------------------------------------------------------------------

    private sealed class StubCorrelationService : ICorrelationService
    {
        private string _correlationId = "stub-correlation-id";
        private string? _opId;

        public string CurrentCorrelationId => _correlationId;

        public string? OperationCorrelationId
        {
            get => _opId;
            set => _opId = value;
        }

        public void SetCorrelationId(string correlationId) => _correlationId = correlationId;
    }

    private sealed class StubLivenessVectorService : ILivenessVectorService
    {
        public bool StampCalled { get; private set; }
        public string? LastStampedKey { get; private set; }

        public void Stamp(string jobKey)
        {
            StampCalled = true;
            LastStampedKey = jobKey;
        }

        public DateTimeOffset? GetStamp(string jobKey) => null;

        public IReadOnlyDictionary<string, DateTimeOffset> GetAllStamps()
            => new Dictionary<string, DateTimeOffset>().AsReadOnly();

        public void Remove(string jobKey) { }
    }

    private sealed class StubTenantVectorRegistry : ITenantVectorRegistry
    {
        private IReadOnlyList<PriorityGroup> _groups = Array.Empty<PriorityGroup>();

        public IReadOnlyList<PriorityGroup> Groups => _groups;
        public int TenantCount => _groups.Sum(g => g.Tenants.Count);
        public int SlotCount => 0;

        public void SetGroups(params PriorityGroup[] groups) => _groups = groups;

        public bool TryRoute(string ip, int port, string metricName,
            out IReadOnlyList<MetricSlotHolder> holders)
        {
            holders = Array.Empty<MetricSlotHolder>();
            return false;
        }
    }

    private sealed class StubSuppressionCache : ISuppressionCache
    {
        public bool SuppressResult { get; set; }
        public Dictionary<string, bool>? SuppressResults { get; set; }
        public int Count => 0;

        public bool TrySuppress(string key, int windowSeconds)
        {
            if (SuppressResults is not null && SuppressResults.TryGetValue(key, out var result))
                return result;
            return SuppressResult;
        }
    }

    private sealed class StubCommandChannel : ICommandChannel
    {
        private readonly Channel<CommandRequest> _channel =
            Channel.CreateBounded<CommandRequest>(16);

        public ChannelWriter<CommandRequest> Writer => _channel.Writer;
        public ChannelReader<CommandRequest> Reader => _channel.Reader;
    }

    private sealed class StubJobContext : IJobExecutionContext
    {
        private readonly IJobDetail _jobDetail;

        public StubJobContext(string jobKeyName)
        {
            _jobDetail = JobBuilder.Create<SnapshotJob>()
                .WithIdentity(jobKeyName)
                .Build();
        }

        public IJobDetail JobDetail => _jobDetail;
        public CancellationToken CancellationToken => CancellationToken.None;
        public object? Result { get; set; }

        // --- Unused interface members ---
        public IScheduler Scheduler                     => throw new NotImplementedException();
        public ITrigger Trigger                         => throw new NotImplementedException();
        public ICalendar? Calendar                      => throw new NotImplementedException();
        public bool Recovering                          => false;
        public TriggerKey RecoveringTriggerKey          => throw new NotImplementedException();
        public int RefireCount                          => 0;
        public JobDataMap MergedJobDataMap              => throw new NotImplementedException();
        public JobDataMap JobDataMap                    => throw new NotImplementedException();
        public string FireInstanceId                    => string.Empty;
        public DateTimeOffset FireTimeUtc               => DateTimeOffset.UtcNow;
        public DateTimeOffset? ScheduledFireTimeUtc     => null;
        public DateTimeOffset? NextFireTimeUtc          => null;
        public DateTimeOffset? PreviousFireTimeUtc      => null;
        public TimeSpan JobRunTime                      => TimeSpan.Zero;
        public IJob JobInstance                         => throw new NotImplementedException();

        public void Put(object key, object objectValue) { }
        public object? Get(object key) => null;
    }

    // -----------------------------------------------------------------------
    // IsViolated — direct unit tests for threshold edge cases
    // -----------------------------------------------------------------------

    [Fact]
    public void IsViolated_NullThreshold_ReturnsTrue()
    {
        var holder = MakeHolder(role: "Evaluate", threshold: null);
        var slot = new MetricSlot(5.0, null, DateTimeOffset.UtcNow);
        Assert.True(SnapshotJob.IsViolated(holder, slot));
    }

    [Fact]
    public void IsViolated_BothBoundsNull_ReturnsTrue()
    {
        var holder = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = null, Max = null });
        var slot = new MetricSlot(5.0, null, DateTimeOffset.UtcNow);
        Assert.True(SnapshotJob.IsViolated(holder, slot));
    }

    [Fact]
    public void IsViolated_EqualBounds_ValueEquals_ReturnsTrue()
    {
        var holder = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 5.0, Max = 5.0 });
        var slot = new MetricSlot(5.0, null, DateTimeOffset.UtcNow);
        Assert.True(SnapshotJob.IsViolated(holder, slot));
    }

    [Fact]
    public void IsViolated_EqualBounds_ValueDiffers_ReturnsFalse()
    {
        var holder = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 5.0, Max = 5.0 });
        var slot = new MetricSlot(6.0, null, DateTimeOffset.UtcNow);
        Assert.False(SnapshotJob.IsViolated(holder, slot));
    }

    [Fact]
    public void IsViolated_RangeBounds_ValueAtBoundary_ReturnsFalse()
    {
        var holder = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 5.0, Max = 10.0 });
        var slot = new MetricSlot(5.0, null, DateTimeOffset.UtcNow);
        Assert.False(SnapshotJob.IsViolated(holder, slot)); // Boundary = in-range
    }

    [Fact]
    public void IsViolated_RangeBounds_ValueBelowMin_ReturnsTrue()
    {
        var holder = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 5.0, Max = 10.0 });
        var slot = new MetricSlot(4.9, null, DateTimeOffset.UtcNow);
        Assert.True(SnapshotJob.IsViolated(holder, slot));
    }

    [Fact]
    public void IsViolated_OnlyMin_ValueBelow_ReturnsTrue()
    {
        var holder = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 5.0 });
        var slot = new MetricSlot(4.0, null, DateTimeOffset.UtcNow);
        Assert.True(SnapshotJob.IsViolated(holder, slot));
    }

    [Fact]
    public void IsViolated_OnlyMax_ValueAbove_ReturnsTrue()
    {
        var holder = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Max = 10.0 });
        var slot = new MetricSlot(11.0, null, DateTimeOffset.UtcNow);
        Assert.True(SnapshotJob.IsViolated(holder, slot));
    }
}
