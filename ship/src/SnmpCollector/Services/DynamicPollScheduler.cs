using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;
using SnmpCollector.Jobs;
using SnmpCollector.Pipeline;

namespace SnmpCollector.Services;

/// <summary>
/// Reconciles Quartz poll jobs at runtime when the device configuration changes.
/// Computes a diff between currently scheduled metric-poll-* jobs and the desired
/// state from <see cref="DeviceOptions"/>, then adds new jobs, removes deleted ones,
/// and reschedules those whose interval changed.
/// <para>
/// Called by <see cref="DeviceWatcherService"/> after <c>DeviceRegistry.ReloadAsync</c>
/// completes. Thread safety is handled by the caller's <see cref="SemaphoreSlim"/> gate.
/// </para>
/// </summary>
public sealed class DynamicPollScheduler
{
    private const string JobPrefix = "metric-poll-";

    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IJobIntervalRegistry _intervalRegistry;
    private readonly ILivenessVectorService _liveness;
    private readonly ILogger<DynamicPollScheduler> _logger;

    public DynamicPollScheduler(
        ISchedulerFactory schedulerFactory,
        IJobIntervalRegistry intervalRegistry,
        ILivenessVectorService liveness,
        ILogger<DynamicPollScheduler> logger)
    {
        _schedulerFactory = schedulerFactory ?? throw new ArgumentNullException(nameof(schedulerFactory));
        _intervalRegistry = intervalRegistry ?? throw new ArgumentNullException(nameof(intervalRegistry));
        _liveness = liveness ?? throw new ArgumentNullException(nameof(liveness));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reconciles Quartz metric-poll jobs to match the desired device configuration.
    /// <list type="bullet">
    /// <item>Removes jobs whose device/poll-group no longer exists.</item>
    /// <item>Adds jobs for new device/poll-group combinations.</item>
    /// <item>Reschedules jobs whose interval has changed.</item>
    /// </list>
    /// </summary>
    /// <param name="resolvedDevices">The desired device list with resolved IPs from DeviceRegistry.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ReconcileAsync(IReadOnlyList<DeviceInfo> resolvedDevices, CancellationToken ct)
    {
        var scheduler = await _schedulerFactory.GetScheduler(ct).ConfigureAwait(false);

        // 1. Collect current metric-poll-* job keys
        var currentJobKeys = await scheduler.GetJobKeys(
            GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup), ct).ConfigureAwait(false);

        var existingPollKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in currentJobKeys)
        {
            if (key.Name.StartsWith(JobPrefix, StringComparison.Ordinal))
                existingPollKeys.Add(key.Name);
        }

        // 2. Build desired job set using config addresses (DNS or IP as configured)
        var desiredJobs = new Dictionary<string, (DeviceInfo Device, int PollIndex, MetricPollInfo Poll)>(StringComparer.Ordinal);
        foreach (var device in resolvedDevices)
        {
            for (var pi = 0; pi < device.PollGroups.Count; pi++)
            {
                var jobName = $"{JobPrefix}{device.ConfigAddress}_{device.Port}-{pi}";
                desiredJobs[jobName] = (device, pi, device.PollGroups[pi]);
            }
        }

        // 3. Compute diff
        var toRemove = existingPollKeys.Except(desiredJobs.Keys).ToList();
        var toAdd = desiredJobs.Keys.Except(existingPollKeys).ToList();
        var toCheck = existingPollKeys.Intersect(desiredJobs.Keys).ToList();

        // 4. Remove stale jobs
        foreach (var jobName in toRemove)
        {
            await scheduler.DeleteJob(new JobKey(jobName), ct).ConfigureAwait(false);
            _intervalRegistry.Unregister(jobName);
            _liveness.Remove(jobName);
        }

        // 5. Add new jobs
        foreach (var jobName in toAdd)
        {
            var (device, pollIndex, poll) = desiredJobs[jobName];
            await ScheduleJobAsync(scheduler, jobName, device, pollIndex, poll, ct).ConfigureAwait(false);
        }

        // 6. Reschedule changed intervals
        var rescheduled = 0;
        foreach (var jobName in toCheck)
        {
            var (device, pollIndex, poll) = desiredJobs[jobName];

            if (_intervalRegistry.TryGetInterval(jobName, out var currentInterval)
                && currentInterval == poll.IntervalSeconds)
            {
                continue; // Interval unchanged
            }

            // Reschedule: replace trigger with new interval
            var triggerKey = new TriggerKey($"{jobName}-trigger");
            var newTrigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(new JobKey(jobName))
                .StartNow()
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(poll.IntervalSeconds)
                    .RepeatForever()
                    .WithMisfireHandlingInstructionNextWithRemainingCount())
                .Build();

            await scheduler.RescheduleJob(triggerKey, newTrigger, ct).ConfigureAwait(false);
            _intervalRegistry.Register(jobName, poll.IntervalSeconds);
            rescheduled++;
        }

        _logger.LogInformation(
            "Poll scheduler reconciled: +{Added} added, -{Removed} removed, ~{Rescheduled} rescheduled, {Total} total jobs",
            toAdd.Count,
            toRemove.Count,
            rescheduled,
            desiredJobs.Count);
    }

    private async Task ScheduleJobAsync(
        IScheduler scheduler,
        string jobName,
        DeviceInfo device,
        int pollIndex,
        MetricPollInfo poll,
        CancellationToken ct)
    {
        var jobKey = new JobKey(jobName);
        var job = JobBuilder.Create<MetricPollJob>()
            .WithIdentity(jobKey)
            .UsingJobData("configAddress", device.ConfigAddress)
            .UsingJobData("port", device.Port)
            .UsingJobData("pollIndex", pollIndex)
            .UsingJobData("intervalSeconds", poll.IntervalSeconds)
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{jobName}-trigger")
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(s => s
                .WithIntervalInSeconds(poll.IntervalSeconds)
                .RepeatForever()
                .WithMisfireHandlingInstructionNextWithRemainingCount())
            .Build();

        await scheduler.ScheduleJob(job, trigger, ct).ConfigureAwait(false);
        _intervalRegistry.Register(jobName, poll.IntervalSeconds);
    }
}
