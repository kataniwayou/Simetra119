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
        double graceMultiplier = 2.0)
    {
        var options = Options.Create(new LivenessOptions { GraceMultiplier = graceMultiplier });
        return new LivenessHealthCheck(
            liveness, intervals, options,
            NullLogger<LivenessHealthCheck>.Instance);
    }

    [Fact]
    public async Task ReturnsHealthy_WhenNoStampsExist()
    {
        var liveness = new LivenessVectorService();
        var intervals = new JobIntervalRegistry();

        var check = CreateCheck(liveness, intervals);
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

        var check = CreateCheck(liveness, intervals);
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

        var check = CreateCheck(liveness, intervals);
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

        var check = CreateCheck(liveness, intervals);
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

        var check = CreateCheck(liveness, intervals);
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

        var check = CreateCheck(liveness, intervals, graceMultiplier: 5.0);
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

        var check = CreateCheck(liveness, intervals);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("2 stale", result.Description);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.ContainsKey("job-a"));
        Assert.True(result.Data.ContainsKey("job-b"));
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
    }
}
