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
    public void EvaluateTenant_NullReadSlot_NotStale()
    {
        // Holder with no value written — ReadSlot returns null, should not be stale
        var holder = MakeHolder(intervalSeconds: 1, graceMultiplier: 1.0, role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        // Do NOT write any value

        var tenant = MakeTenant(holder);
        var result = _job.EvaluateTenant(tenant);

        // Null ReadSlot means not stale (skipped), but also no Resolved data
        // so AreAllResolvedViolated returns vacuous true → ConfirmedBad
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
    public void EvaluateTenant_ResolvedNullReadSlot_ExcludedFromGate()
    {
        // One Resolved with data (violated), one with null ReadSlot (excluded)
        var h1 = MakeHolder(intervalSeconds: 3600, role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(h1, 5.0); // Violated

        var h2 = MakeHolder(intervalSeconds: 3600, role: "Resolved",
            threshold: new ThresholdOptions { Min = 10 });
        // h2 has no value written → null ReadSlot → excluded from check

        var tenant = MakeTenant(h1, h2);
        var result = _job.EvaluateTenant(tenant);

        // h2 excluded, h1 violated → all checked Resolved are violated → ConfirmedBad
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
    public void Execute_EvaluateNullReadSlot_ExcludedFromCheck()
    {
        var resolved = MakeHolder(role: "Resolved",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        WriteValue(resolved, 50.0);

        // Evaluate holder with data → violated
        var eval1 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 10 });
        WriteValue(eval1, 5.0);

        // Evaluate holder with null ReadSlot → excluded, does not prevent commanding
        var eval2 = MakeHolder(role: "Evaluate",
            threshold: new ThresholdOptions { Min = 0, Max = 100 });
        // No value written → null ReadSlot

        var cmd = new CommandSlotOptions
            { Ip = "10.0.0.2", Port = 161, CommandName = "reset", Value = "1", ValueType = "Integer32" };
        var tenant = MakeTenant(new[] { resolved, eval1, eval2 }, new[] { cmd });

        _job.EvaluateTenant(tenant);

        // eval2 excluded, eval1 violated → all checked are violated → Tier 4
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
        Assert.Equal("tenant-abc", request.DeviceName);
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
            _commandChannel.Writer.TryWrite(new CommandRequest("x", 1, "fill", "0", "Integer32", "filler"));

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
        Assert.Equal("tenant-b", request!.DeviceName);
        Assert.False(_commandChannel.Reader.TryRead(out _)); // Only one
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
