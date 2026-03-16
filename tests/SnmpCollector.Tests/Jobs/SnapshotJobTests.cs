using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Lextm.SharpSnmpLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quartz;
using SnmpCollector.Configuration;
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
    private readonly StubTenantVectorRegistry _registry = new();
    private readonly StubSuppressionCache _suppressionCache = new();
    private readonly StubCommandChannel _commandChannel = new();
    private readonly StubCorrelationService _correlation = new();
    private readonly StubLivenessVectorService _liveness = new();
    private readonly SnapshotJob _job;

    public SnapshotJobTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        _sp = services.BuildServiceProvider();

        _pipelineMetrics = new PipelineMetricService(
            _sp.GetRequiredService<IMeterFactory>());

        _job = new SnapshotJob(
            _registry,
            _suppressionCache,
            _commandChannel,
            _correlation,
            _liveness,
            _pipelineMetrics,
            Options.Create(new SnapshotJobOptions()),
            NullLogger<SnapshotJob>.Instance);
    }

    public void Dispose()
    {
        _pipelineMetrics.Dispose();
        _sp.Dispose();
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

        Assert.NotEqual(SnapshotJob.TierResult.Stale, result);
    }

    [Fact]
    public void EvaluateTenant_SentinelReadSlot_NotStaleWithinGrace()
    {
        // Holder with only sentinel (no real write). Sentinel timestamp is from construction.
        // With intervalSeconds=3600 and graceMultiplier=2.0, grace window is 7200s — sentinel is fresh.
        var holder = MakeHolder(intervalSeconds: 3600, graceMultiplier: 2.0, role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        // Do NOT write any value — sentinel has Value=0, in range [0,100] → not violated

        var tenant = MakeTenant(holder);
        var result = _job.EvaluateTenant(tenant);

        // Sentinel within grace → not stale. Sentinel Value=0 in range → not violated → Healthy
        Assert.NotEqual(SnapshotJob.TierResult.Stale, result);
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
        Assert.NotEqual(SnapshotJob.TierResult.Stale, result);
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

        Assert.NotEqual(SnapshotJob.TierResult.Stale, result);
    }

    [Fact]
    public async Task EvaluateTenant_StaleHolder_ReturnsStale()
    {
        // Very small interval + grace = 1 * 1.0 = 1 second window
        var holder = MakeHolder(intervalSeconds: 1, graceMultiplier: 1.0, role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(holder, 50.0);

        // Wait for value to become stale (> 1 second)
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        var tenant = MakeTenant(holder);
        var result = _job.EvaluateTenant(tenant);

        Assert.Equal(SnapshotJob.TierResult.Stale, result);
    }

    // -------------------------------------------------------------------------
    // Tier 2: Resolved gate tests
    // -------------------------------------------------------------------------

    [Fact]
    public void EvaluateTenant_AllResolvedViolated_ConfirmedBadNoCommands()
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

        Assert.Equal(SnapshotJob.TierResult.ConfirmedBad, result);
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
        Assert.NotEqual(SnapshotJob.TierResult.ConfirmedBad, result);
        Assert.NotEqual(SnapshotJob.TierResult.Stale, result);
    }

    [Fact]
    public void EvaluateTenant_ResolvedSentinelReadSlot_ParticipatesInGate()
    {
        // One Resolved with data (violated), one with sentinel only
        var h1 = MakeHolder(intervalSeconds: 3600, role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(h1, 5.0); // Below Min=10 → violated

        var h2 = MakeHolder(intervalSeconds: 3600, role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 });
        // h2 has sentinel Value=0. 0 < 10 → violated

        var tenant = MakeTenant(h1, h2);
        var result = _job.EvaluateTenant(tenant);

        // h1 violated, h2 sentinel (Value=0 < Min=10) violated → all Resolved violated → ConfirmedBad
        Assert.Equal(SnapshotJob.TierResult.ConfirmedBad, result);
    }

    [Fact]
    public void EvaluateTenant_ResolvedNoThreshold_TreatedAsViolated()
    {
        // Resolved holder with null threshold → treated as violated
        var holder = MakeHolder(intervalSeconds: 3600, role: "Resolved", threshold: null);
        WriteValue(holder, 50.0);

        var tenant = MakeTenant(holder);
        var result = _job.EvaluateTenant(tenant);

        Assert.Equal(SnapshotJob.TierResult.ConfirmedBad, result);
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

        Assert.Equal(SnapshotJob.TierResult.ConfirmedBad, result);
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
        Assert.NotEqual(SnapshotJob.TierResult.ConfirmedBad, result);
        Assert.NotEqual(SnapshotJob.TierResult.Stale, result);
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

        Assert.NotEqual(SnapshotJob.TierResult.ConfirmedBad, result);
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

        Assert.Equal(SnapshotJob.TierResult.Healthy, result);
        Assert.False(_commandChannel.Reader.TryRead(out _)); // No commands
    }

    [Fact]
    public void Execute_EvaluateSentinelReadSlot_ParticipatesInCheck()
    {
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0);

        // Evaluate holder with data → violated
        var eval1 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(eval1, 5.0);

        // Evaluate holder with sentinel only. Sentinel Value=0, threshold Min=10 → 0 < 10 → violated
        var eval2 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 });
        // No real write — sentinel (Value=0) participates

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { resolved, eval1, eval2 }, new[] { cmd });

        _job.EvaluateTenant(tenant);

        // eval1 violated, eval2 sentinel (Value=0 < Min=10) violated → all Evaluate violated → Tier 4
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

        Assert.Equal(SnapshotJob.TierResult.Healthy, result);
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

        // Suppressed → no TryWrite, zero enqueued → ConfirmedBad (safe to cascade)
        Assert.False(_commandChannel.Reader.TryRead(out _));
        Assert.Equal(SnapshotJob.TierResult.ConfirmedBad, result);
    }

    [Fact]
    public void Execute_ChannelFull_IncrementFailed_NoException()
    {
        // Fill the channel to capacity (16)
        for (var i = 0; i < 16; i++)
            _commandChannel.Writer.TryWrite(new CommandRequest("x", 1, "fill", "0", "Integer32"));

        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0);

        var eval1 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(eval1, 5.0);

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { resolved, eval1 }, new[] { cmd });

        // Should not throw — channel full means zero enqueued → ConfirmedBad
        var result = _job.EvaluateTenant(tenant);

        Assert.Equal(SnapshotJob.TierResult.ConfirmedBad, result);
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
    public async Task Execute_SingleGroupOneStale_StaleDetected()
    {
        // One stale tenant in the group
        var staleHolder = MakeHolder(intervalSeconds: 1, graceMultiplier: 0.001, role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(staleHolder, 50.0);
        // Force staleness by writing a value with an old timestamp
        // Instead, use a very small interval so the value is immediately stale
        // IntervalSeconds=1, GraceMultiplier=0.001 → grace window = 0.001s (1ms)
        // The WriteValue call just happened, so we need to wait briefly
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        var staleTenant = MakeTenant(new[] { staleHolder }, Array.Empty<CommandSlotOptions>(), id: "stale-t");
        var healthyTenant = MakeHealthyTenant("healthy-t");

        _registry.SetGroups(new PriorityGroup(1, new[] { staleTenant, healthyTenant }));
        await _job.Execute(MakeContext("snapshot"));

        // Stale tenant detected, no commands
        Assert.False(_commandChannel.Reader.TryRead(out _));
        Assert.True(_liveness.StampCalled);
    }

    [Fact]
    public async Task Execute_TwoGroups_FirstAllConfirmedBad_SecondGroupEvaluated()
    {
        // Group 1: ALL Resolved violated → ConfirmedBad (Tier 2 stop). Advance gate passes.
        var confirmedBadTenant = MakeConfirmedBadTenant("cb-t");
        // Group 2: Healthy tenant (should be evaluated since group 1 advances)
        var healthyTenant = MakeHealthyTenant("healthy-t");

        _registry.SetGroups(
            new PriorityGroup(1, new[] { confirmedBadTenant }),
            new PriorityGroup(2, new[] { healthyTenant }));

        await _job.Execute(MakeContext("snapshot"));

        // No commands from either group (ConfirmedBad stops at Tier 2, Healthy stops at Tier 3)
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
        // Group 1: Stale tenant → advance gate blocks
        var staleHolder = MakeHolder(intervalSeconds: 1, graceMultiplier: 0.001, role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(staleHolder, 50.0);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        var staleTenant = MakeTenant(new[] { staleHolder }, Array.Empty<CommandSlotOptions>(), id: "stale-t");

        // Group 2: Commanding tenant — would enqueue if evaluated
        var group2Tenant = MakeCommandingTenant("cmd-t2", "cmd-group2");

        _registry.SetGroups(
            new PriorityGroup(1, new[] { staleTenant }),
            new PriorityGroup(2, new[] { group2Tenant }));

        await _job.Execute(MakeContext("snapshot"));

        // No commands — group 1 blocked, group 2 not evaluated
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
    public async Task Execute_TwoGroups_FirstMixedHealthyAndConfirmedBad_SecondGroupEvaluated()
    {
        // Group 1: Mixed Healthy + ConfirmedBad → both are advance-allowing states
        var healthyTenant = MakeHealthyTenant("h1");
        var confirmedBadTenant = MakeConfirmedBadTenant("cb1");
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
    // Helpers
    // -------------------------------------------------------------------------

    private static MetricSlotHolder MakeHolder(
        int intervalSeconds = 3600,
        double graceMultiplier = 2.0,
        string role = "Resolved",
        ThresholdOptions? threshold = null,
        string metricName = "test-metric")
    {
        return new MetricSlotHolder(
            ip: "10.0.0.1",
            port: 161,
            metricName: metricName,
            intervalSeconds: intervalSeconds,
            role: role,
            timeSeriesSize: 1,
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
    /// Creates a tenant that evaluates as ConfirmedBad (Tier 2 stop):
    /// ALL Resolved violated → ConfirmedBad.
    /// </summary>
    private static Tenant MakeConfirmedBadTenant(string id)
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
}
