using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;
using SnmpCollector.HealthChecks;
using SnmpCollector.Pipeline;
using Xunit;

namespace SnmpCollector.Tests.HealthChecks;

public sealed class LivenessHealthCheckTests
{
    private static LivenessHealthCheck CreateCheck(
        ILivenessVectorService liveness,
        IJobIntervalRegistry intervals,
        double graceMultiplier = 2.0,
        IHeartbeatLivenessService? heartbeatLiveness = null,
        int heartbeatIntervalSeconds = 15)
    {
        var options = Options.Create(new LivenessOptions { GraceMultiplier = graceMultiplier });
        var heartbeatOptions = Options.Create(new SnmpHeartbeatJobOptions { IntervalSeconds = heartbeatIntervalSeconds });
        return new LivenessHealthCheck(
            liveness, intervals, options,
            heartbeatLiveness ?? new HeartbeatLivenessService(),
            heartbeatOptions,
            NullLogger<LivenessHealthCheck>.Instance);
    }

    /// <summary>
    /// Returns a HeartbeatLivenessService that has just been stamped (fresh).
    /// Use this in tests that only care about job-stamp behavior, not pipeline liveness.
    /// </summary>
    private static HeartbeatLivenessService CreateFreshHeartbeatLiveness()
    {
        var svc = new HeartbeatLivenessService();
        svc.Stamp();
        return svc;
    }

    [Fact]
    public async Task ReturnsHealthy_WhenNoStampsExist()
    {
        var liveness = new LivenessVectorService();
        var intervals = new JobIntervalRegistry();

        var check = CreateCheck(liveness, intervals, heartbeatLiveness: CreateFreshHeartbeatLiveness());
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task ReturnsHealthy_WhenAllStampsFresh()
    {
        var liveness = new LivenessVectorService();
        var intervals = new JobIntervalRegistry();

        intervals.Register("correlation", 30);
        intervals.Register("metric-poll-sw1-0", 60);
        liveness.Stamp("correlation");
        liveness.Stamp("metric-poll-sw1-0");

        var check = CreateCheck(liveness, intervals, heartbeatLiveness: CreateFreshHeartbeatLiveness());
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task ReturnsUnhealthy_WhenStampIsStale()
    {
        var liveness = new StaleVectorService(new Dictionary<string, DateTimeOffset>
        {
            ["correlation"] = DateTimeOffset.UtcNow.AddSeconds(-120)
        });
        var intervals = new JobIntervalRegistry();
        intervals.Register("correlation", 30);

        var check = CreateCheck(liveness, intervals, heartbeatLiveness: CreateFreshHeartbeatLiveness());
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("stale", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReturnsHealthy_WhenStampWithinThreshold()
    {
        var liveness = new StaleVectorService(new Dictionary<string, DateTimeOffset>
        {
            ["correlation"] = DateTimeOffset.UtcNow.AddSeconds(-10)
        });
        var intervals = new JobIntervalRegistry();
        intervals.Register("correlation", 30);

        var check = CreateCheck(liveness, intervals, heartbeatLiveness: CreateFreshHeartbeatLiveness());
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task SkipsStamps_WithUnknownJobKeys()
    {
        var liveness = new StaleVectorService(new Dictionary<string, DateTimeOffset>
        {
            ["unknown-job"] = DateTimeOffset.UtcNow.AddSeconds(-9999)
        });
        var intervals = new JobIntervalRegistry();

        var check = CreateCheck(liveness, intervals, heartbeatLiveness: CreateFreshHeartbeatLiveness());
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task RespectsCustomGraceMultiplier()
    {
        var liveness = new StaleVectorService(new Dictionary<string, DateTimeOffset>
        {
            ["correlation"] = DateTimeOffset.UtcNow.AddSeconds(-90)
        });
        var intervals = new JobIntervalRegistry();
        intervals.Register("correlation", 30);

        var check = CreateCheck(liveness, intervals, graceMultiplier: 5.0, heartbeatLiveness: CreateFreshHeartbeatLiveness());
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task MultipleStaleJobs_AllReported()
    {
        var liveness = new StaleVectorService(new Dictionary<string, DateTimeOffset>
        {
            ["job-a"] = DateTimeOffset.UtcNow.AddSeconds(-200),
            ["job-b"] = DateTimeOffset.UtcNow.AddSeconds(-300)
        });
        var intervals = new JobIntervalRegistry();
        intervals.Register("job-a", 30);
        intervals.Register("job-b", 30);

        var check = CreateCheck(liveness, intervals, heartbeatLiveness: CreateFreshHeartbeatLiveness());
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("2 stale", result.Description);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.ContainsKey("job-a"));
        Assert.True(result.Data.ContainsKey("job-b"));
    }

    // --- Pipeline liveness tests ---

    [Fact]
    public async Task ReturnsHealthy_WhenPipelineHeartbeatFresh()
    {
        var liveness = new LivenessVectorService();
        var intervals = new JobIntervalRegistry();
        var heartbeatLiveness = new HeartbeatLivenessService();
        heartbeatLiveness.Stamp(); // fresh stamp

        var check = CreateCheck(liveness, intervals, heartbeatLiveness: heartbeatLiveness);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.ContainsKey("pipeline-heartbeat"));
    }

    [Fact]
    public async Task ReturnsUnhealthy_WhenPipelineHeartbeatNeverStamped()
    {
        var liveness = new LivenessVectorService();
        var intervals = new JobIntervalRegistry();
        var heartbeatLiveness = new HeartbeatLivenessService(); // never stamped

        var check = CreateCheck(liveness, intervals, heartbeatLiveness: heartbeatLiveness);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.ContainsKey("pipeline-heartbeat"));
    }

    [Fact]
    public async Task ReturnsUnhealthy_WhenPipelineHeartbeatStale()
    {
        var liveness = new LivenessVectorService();
        var intervals = new JobIntervalRegistry();

        // Stale: stamp is 60s old, threshold = 15s * 2.0 = 30s
        var heartbeatLiveness = new StaleHeartbeatLivenessService(
            DateTimeOffset.UtcNow.AddSeconds(-60));

        var check = CreateCheck(liveness, intervals,
            heartbeatLiveness: heartbeatLiveness, heartbeatIntervalSeconds: 15);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("stale", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.ContainsKey("pipeline-heartbeat"));
    }

    [Fact]
    public async Task ReturnsUnhealthy_WhenJobsFreshButPipelineStale()
    {
        var liveness = new LivenessVectorService();
        var intervals = new JobIntervalRegistry();

        intervals.Register("correlation", 30);
        liveness.Stamp("correlation"); // fresh job stamp

        var heartbeatLiveness = new StaleHeartbeatLivenessService(
            DateTimeOffset.UtcNow.AddSeconds(-120)); // stale pipeline

        var check = CreateCheck(liveness, intervals,
            heartbeatLiveness: heartbeatLiveness, heartbeatIntervalSeconds: 15);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.ContainsKey("pipeline-heartbeat"));
        Assert.True(result.Data.ContainsKey("correlation"));
    }

    private sealed class StaleVectorService : ILivenessVectorService
    {
        private readonly Dictionary<string, DateTimeOffset> _stamps;

        public StaleVectorService(Dictionary<string, DateTimeOffset> stamps)
            => _stamps = stamps;

        public void Stamp(string jobKey) => _stamps[jobKey] = DateTimeOffset.UtcNow;

        public DateTimeOffset? GetStamp(string jobKey)
            => _stamps.TryGetValue(jobKey, out var ts) ? ts : null;

        public IReadOnlyDictionary<string, DateTimeOffset> GetAllStamps()
            => _stamps.AsReadOnly();

        public void Remove(string jobKey) => _stamps.Remove(jobKey);
    }

    private sealed class StaleHeartbeatLivenessService : IHeartbeatLivenessService
    {
        private readonly DateTimeOffset? _lastArrival;

        public StaleHeartbeatLivenessService(DateTimeOffset? lastArrival)
            => _lastArrival = lastArrival;

        public void Stamp() { }

        public DateTimeOffset? LastArrival => _lastArrival;
    }
}
