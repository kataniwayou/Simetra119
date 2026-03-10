using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using Quartz.Impl.Matchers;
using SnmpCollector.Pipeline;
using SnmpCollector.Services;
using Xunit;

namespace SnmpCollector.Tests.Services;

public sealed class DynamicPollSchedulerTests
{
    private readonly IScheduler _scheduler = Substitute.For<IScheduler>();
    private readonly ISchedulerFactory _schedulerFactory = Substitute.For<ISchedulerFactory>();
    private readonly IJobIntervalRegistry _intervalRegistry = Substitute.For<IJobIntervalRegistry>();
    private readonly ILivenessVectorService _livenessVector = Substitute.For<ILivenessVectorService>();
    private readonly DynamicPollScheduler _sut;

    public DynamicPollSchedulerTests()
    {
        _schedulerFactory.GetScheduler(Arg.Any<CancellationToken>()).Returns(_scheduler);
        _sut = new DynamicPollScheduler(
            _schedulerFactory,
            _intervalRegistry,
            _livenessVector,
            NullLogger<DynamicPollScheduler>.Instance);
    }

    private static DeviceInfo MakeDevice(string name, string ip, int port, int intervalSeconds, int pollCount = 1)
    {
        var polls = new List<MetricPollInfo>();
        for (int i = 0; i < pollCount; i++)
        {
            polls.Add(new MetricPollInfo(
                PollIndex: i,
                Oids: new List<string> { "1.3.6.1.2.1.1.1.0" }.AsReadOnly(),
                IntervalSeconds: intervalSeconds));
        }
        return new DeviceInfo(name, ip, ip, port, polls.AsReadOnly(), $"test.{name}");
    }

    private void SetupExistingJobs(params string[] jobNames)
    {
        var keys = new HashSet<JobKey>();
        foreach (var name in jobNames)
            keys.Add(new JobKey(name));

        _scheduler.GetJobKeys(Arg.Any<GroupMatcher<JobKey>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyCollection<JobKey>)keys);
    }

    [Fact]
    public async Task ReconcileAsync_WithNewDevices_SchedulesJobs()
    {
        SetupExistingJobs(); // no existing jobs

        await _sut.ReconcileAsync(new[] { MakeDevice("DEV-01", "127.0.0.1", 161, 10) }, CancellationToken.None);

        await _scheduler.Received(1).ScheduleJob(
            Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>());
        _intervalRegistry.Received(1).Register(
            Arg.Is<string>(k => k.Contains("127.0.0.1_161")), 10);
    }

    [Fact]
    public async Task ReconcileAsync_WithRemovedDevices_DeletesJobs()
    {
        SetupExistingJobs("metric-poll-127.0.0.1_161-0");
        _intervalRegistry.TryGetInterval("metric-poll-127.0.0.1_161-0", out Arg.Any<int>())
            .Returns(x => { x[1] = 10; return true; });

        await _sut.ReconcileAsync(Array.Empty<DeviceInfo>(), CancellationToken.None);

        await _scheduler.Received(1).DeleteJob(
            Arg.Is<JobKey>(k => k.Name == "metric-poll-127.0.0.1_161-0"),
            Arg.Any<CancellationToken>());
        _intervalRegistry.Received(1).Unregister("metric-poll-127.0.0.1_161-0");
        _livenessVector.Received(1).Remove("metric-poll-127.0.0.1_161-0");
    }

    [Fact]
    public async Task ReconcileAsync_WithChangedInterval_ReschedulesJob()
    {
        SetupExistingJobs("metric-poll-127.0.0.1_161-0");
        _intervalRegistry.TryGetInterval("metric-poll-127.0.0.1_161-0", out Arg.Any<int>())
            .Returns(x => { x[1] = 10; return true; });

        // Interval changed from 10 to 30
        await _sut.ReconcileAsync(new[] { MakeDevice("DEV-01", "127.0.0.1", 161, 30) }, CancellationToken.None);

        await _scheduler.Received(1).RescheduleJob(
            Arg.Any<TriggerKey>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>());
        _intervalRegistry.Received(1).Register(
            Arg.Is<string>(k => k.Contains("127.0.0.1_161")), 30);
    }

    [Fact]
    public async Task ReconcileAsync_WithUnchangedDevices_NoChanges()
    {
        SetupExistingJobs("metric-poll-127.0.0.1_161-0");
        _intervalRegistry.TryGetInterval("metric-poll-127.0.0.1_161-0", out Arg.Any<int>())
            .Returns(x => { x[1] = 10; return true; });

        // Same interval = no changes
        await _sut.ReconcileAsync(new[] { MakeDevice("DEV-01", "127.0.0.1", 161, 10) }, CancellationToken.None);

        await _scheduler.DidNotReceive().ScheduleJob(
            Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>());
        await _scheduler.DidNotReceive().DeleteJob(
            Arg.Any<JobKey>(), Arg.Any<CancellationToken>());
        await _scheduler.DidNotReceive().RescheduleJob(
            Arg.Any<TriggerKey>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>());
    }
}
