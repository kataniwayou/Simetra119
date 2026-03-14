using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Net;
using Lextm.SharpSnmpLib;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using Quartz.Impl;
using SnmpCollector.Configuration;
using SnmpCollector.Jobs;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;
using SnmpCollector.Tests.Helpers;
using Xunit;

namespace SnmpCollector.Tests.Jobs;

/// <summary>
/// Unit tests for <see cref="MetricPollJob"/> covering:
/// device-not-found early return, successful dispatch with varbind routing,
/// noSuchObject skip, timeout failure recording, unreachable transition after 3 failures,
/// recovery after unreachable, and PollExecuted counter behavior.
///
/// Uses <see cref="StubSnmpClient"/> instead of real UDP calls; <see cref="CapturingSender"/>
/// captures all ISender.Send calls for assertion.
///
/// Placed in NonParallelMeterTests collection because MeterListener is a global listener;
/// parallel test classes using the same meter name cause cross-test measurement contamination.
/// </summary>
[Collection(NonParallelCollection.Name)]
public sealed class MetricPollJobTests : IDisposable
{
    private const string DeviceName       = "test-router";
    private const string DeviceIp         = "192.168.1.1";
    private const int    DevicePort       = 161;
    private const string IfInOctetsOid    = "1.3.6.1.2.1.2.2.1.10.1";
    private const string IfOutOctetsOid   = "1.3.6.1.2.1.2.2.1.16.1";

    // -------------------------------------------------------------------------
    // Test infrastructure
    // -------------------------------------------------------------------------

    private readonly ServiceProvider _sp;
    private readonly PipelineMetricService _metrics;
    private readonly MeterListener _meterListener;
    private readonly List<(string InstrumentName, long Value, KeyValuePair<string, object?>[] Tags)> _measurements = new();

    public MetricPollJobTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        _sp = services.BuildServiceProvider();

        _metrics = new PipelineMetricService(
            _sp.GetRequiredService<IMeterFactory>());

        _meterListener = new MeterListener();
        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TelemetryConstants.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            _measurements.Add((instrument.Name, value, tags.ToArray()));
        });
        _meterListener.Start();
    }

    public void Dispose()
    {
        _meterListener.Dispose();
        _metrics.Dispose();
        _sp.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helper factories
    // -------------------------------------------------------------------------

    private static DeviceInfo MakeDevice(params string[] pollOids)
    {
        var pollGroup = new MetricPollInfo(0, pollOids.ToList(), 30);
        return new DeviceInfo(DeviceName, DeviceIp, DeviceIp, DevicePort, [pollGroup], $"Simetra.{DeviceName}");
    }

    private MetricPollJob CreateJob(
        IDeviceRegistry? registry = null,
        ISnmpClient? snmpClient = null,
        ISender? sender = null,
        IDeviceUnreachabilityTracker? tracker = null)
    {
        return new MetricPollJob(
            registry    ?? new StubDeviceRegistry([MakeDevice(IfInOctetsOid)]),
            tracker     ?? new DeviceUnreachabilityTracker(),
            sender      ?? new CapturingSender(),
            snmpClient  ?? new StubSnmpClient(),
            new RotatingCorrelationService(),
            new LivenessVectorService(),
            _metrics,
            NullLogger<MetricPollJob>.Instance);
    }

    private static IJobExecutionContext MakeContext(
        string configAddress = DeviceIp,
        int port = DevicePort,
        int pollIndex = 0,
        int intervalSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        return new StubJobExecutionContext(configAddress, port, pollIndex, intervalSeconds, cancellationToken);
    }

    private long CountPollExecuted()
        => _measurements.Count(m => m.InstrumentName == "snmp.poll.executed");

    // -------------------------------------------------------------------------
    // Test 1: Device not found -- logs warning, returns without incrementing counter
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_DeviceNotFound_DoesNotIncrementPollExecuted()
    {
        // Arrange -- registry with NO devices
        var registry = new StubDeviceRegistry([]);
        var sender   = new CapturingSender();
        var job      = CreateJob(registry: registry, sender: sender);
        var context  = MakeContext("99.99.99.99", 9999);

        // Act
        await job.Execute(context);

        // Assert -- no sends, no counter
        Assert.Empty(sender.Sent);
        Assert.Equal(0, CountPollExecuted());
    }

    // -------------------------------------------------------------------------
    // Test 2: Successful poll dispatches every varbind via ISender.Send
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_SuccessfulPoll_DispatchesEachVarbindViaSender()
    {
        // Arrange -- 2 OIDs in response (no sysUpTime prepend)
        var response = new List<Variable>
        {
            new Variable(new ObjectIdentifier(IfInOctetsOid),  new Gauge32(1000)),
            new Variable(new ObjectIdentifier(IfOutOctetsOid), new Gauge32(2000)),
        };

        var snmpClient = new StubSnmpClient { Response = response };
        var sender     = new CapturingSender();
        var job        = CreateJob(
            registry:   new StubDeviceRegistry([MakeDevice(IfInOctetsOid, IfOutOctetsOid)]),
            snmpClient: snmpClient,
            sender:     sender);

        // Act
        await job.Execute(MakeContext());

        // Assert -- both varbinds dispatched
        Assert.Equal(2, sender.Sent.Count);

        var oids = sender.Sent.Select(m => m.Oid).ToList();
        Assert.Contains(IfInOctetsOid, oids);
        Assert.Contains(IfOutOctetsOid, oids);

        // Each message has correct device fields
        foreach (var msg in sender.Sent)
        {
            Assert.Equal(DeviceName, msg.DeviceName);
            Assert.Equal(IPAddress.Parse(DeviceIp), msg.AgentIp);
            Assert.Equal(SnmpSource.Poll, msg.Source);
        }
    }

    // -------------------------------------------------------------------------
    // Test 3: Device Port and CommunityString used directly
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_UsesDevicePortAndCommunityString()
    {
        // Arrange -- device with custom port
        var pollGroup = new MetricPollInfo(0, [IfInOctetsOid], 30);
        var device = new DeviceInfo("custom-device", "10.0.0.99", "10.0.0.99", 1161, [pollGroup], "Simetra.custom-device");

        var snmpClient = new StubSnmpClient { Response = new List<Variable>() };
        var sender     = new CapturingSender();
        var job        = CreateJob(
            registry:   new StubDeviceRegistry([device]),
            snmpClient: snmpClient,
            sender:     sender);

        // Act
        await job.Execute(MakeContext(configAddress: "10.0.0.99", port: 1161));

        // Assert -- StubSnmpClient captured endpoint and community
        Assert.Equal(1161, snmpClient.LastEndpoint!.Port);
        Assert.Equal(IPAddress.Parse("10.0.0.99"), snmpClient.LastEndpoint.Address);
        Assert.Equal("Simetra.custom-device", snmpClient.LastCommunity!.ToString());
    }

    // -------------------------------------------------------------------------
    // Test 4: noSuchObject and noSuchInstance varbinds are skipped
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_NoSuchObject_SkipsVarbind()
    {
        // Arrange -- two error-sentinel varbinds, no valid data OIDs
        var response = new List<Variable>
        {
            new Variable(new ObjectIdentifier(IfInOctetsOid),  new NoSuchObject()),
            new Variable(new ObjectIdentifier(IfOutOctetsOid), new NoSuchInstance()),
        };

        var snmpClient = new StubSnmpClient { Response = response };
        var sender     = new CapturingSender();
        var job        = CreateJob(
            registry:   new StubDeviceRegistry([MakeDevice(IfInOctetsOid, IfOutOctetsOid)]),
            snmpClient: snmpClient,
            sender:     sender);

        // Act
        await job.Execute(MakeContext());

        // Assert -- no sends (both skipped); counter still increments (poll did execute)
        Assert.Empty(sender.Sent);
        Assert.Equal(1, CountPollExecuted());
    }

    // -------------------------------------------------------------------------
    // Test 5: Timeout (OperationCanceledException, host NOT shutting down) records failure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_Timeout_RecordsFailureAndIncrementsPollExecuted()
    {
        // Arrange -- snmp client throws; context cancellation token is NOT cancelled (not shutdown)
        var snmpClient = new StubSnmpClient
        {
            ExceptionToThrow = new OperationCanceledException("timeout")
        };
        var tracker = new DeviceUnreachabilityTracker();
        var job     = CreateJob(snmpClient: snmpClient, tracker: tracker);
        var context = MakeContext(cancellationToken: CancellationToken.None);

        // Act
        await job.Execute(context);

        // Assert
        Assert.Equal(1, tracker.GetFailureCount(DeviceName));
        Assert.False(tracker.IsUnreachable(DeviceName));
        Assert.Equal(1, CountPollExecuted());
    }

    // -------------------------------------------------------------------------
    // Test 6: 3 consecutive timeouts transition device to unreachable
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_ThreeTimeouts_TransitionsToUnreachable()
    {
        var snmpClient = new StubSnmpClient
        {
            ExceptionToThrow = new OperationCanceledException("timeout")
        };
        var tracker = new DeviceUnreachabilityTracker();
        var job     = CreateJob(snmpClient: snmpClient, tracker: tracker);
        var context = MakeContext(cancellationToken: CancellationToken.None);

        await job.Execute(context);
        await job.Execute(context);
        await job.Execute(context);

        Assert.True(tracker.IsUnreachable(DeviceName));
        Assert.Equal(3, CountPollExecuted());
    }

    // -------------------------------------------------------------------------
    // Test 7: Recovery after unreachable -- tracker transitions back to healthy
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_RecoveryAfterUnreachable_TransitionsToHealthy()
    {
        var timeoutClient = new StubSnmpClient
        {
            ExceptionToThrow = new OperationCanceledException("timeout")
        };
        var tracker    = new DeviceUnreachabilityTracker();
        var noopSender = new CapturingSender();
        var ctx        = MakeContext(cancellationToken: CancellationToken.None);

        // Drive to unreachable (3 timeouts)
        var timeoutJob = CreateJob(snmpClient: timeoutClient, tracker: tracker, sender: noopSender);
        await timeoutJob.Execute(ctx);
        await timeoutJob.Execute(ctx);
        await timeoutJob.Execute(ctx);
        Assert.True(tracker.IsUnreachable(DeviceName));

        // One successful poll -- swap to client returning an empty response
        var successClient = new StubSnmpClient { Response = new List<Variable>() };
        var successJob    = CreateJob(snmpClient: successClient, tracker: tracker, sender: noopSender);
        await successJob.Execute(MakeContext());

        Assert.False(tracker.IsUnreachable(DeviceName));
    }

    // -------------------------------------------------------------------------
    // Test 8: CommunityString used directly from DeviceInfo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_CommunityString_UsedDirectly()
    {
        // Arrange -- device with a community string that doesn't follow Simetra.* convention
        // (DeviceInfo is constructed directly in tests; DeviceRegistry would reject this at load time)
        var pollGroup = new MetricPollInfo(0, [IfInOctetsOid], 30);
        var device = new DeviceInfo("custom-device", "10.0.0.99", "10.0.0.99", 1161, [pollGroup], "my-explicit-community");

        var snmpClient = new StubSnmpClient { Response = new List<Variable>() };
        var sender     = new CapturingSender();
        var job        = CreateJob(
            registry:   new StubDeviceRegistry([device]),
            snmpClient: snmpClient,
            sender:     sender);

        // Act
        await job.Execute(MakeContext(configAddress: "10.0.0.99", port: 1161));

        // Assert -- community string is used directly from DeviceInfo, no derivation
        Assert.Equal("my-explicit-community", snmpClient.LastCommunity!.ToString());
    }

    // -------------------------------------------------------------------------
    // Test 9: PollExecuted increments on success and failure, NOT on device-not-found
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_PollExecuted_IncrementsOnSuccessAndFailure_NotOnMissingDevice()
    {
        // --- Successful poll ---
        var successClient = new StubSnmpClient { Response = new List<Variable>() };
        var successJob    = CreateJob(snmpClient: successClient);
        await successJob.Execute(MakeContext());
        Assert.Equal(1, CountPollExecuted());

        // --- General exception (e.g. SNMP network error) ---
        var errorClient = new StubSnmpClient
        {
            ExceptionToThrow = new InvalidOperationException("snmp network error")
        };
        var errorJob = CreateJob(snmpClient: errorClient);
        await errorJob.Execute(MakeContext(cancellationToken: CancellationToken.None));
        Assert.Equal(2, CountPollExecuted());

        // --- Device not found -- must NOT increment ---
        var missingJob = CreateJob(registry: new StubDeviceRegistry([]));
        await missingJob.Execute(MakeContext("99.99.99.99", 9999));
        Assert.Equal(2, CountPollExecuted()); // still 2
    }

    // -------------------------------------------------------------------------
    // Stubs and helpers
    // -------------------------------------------------------------------------

    /// <summary>Minimal IDeviceRegistry that returns devices from a preconfigured list.</summary>
    private sealed class StubDeviceRegistry : IDeviceRegistry
    {
        private readonly IReadOnlyList<DeviceInfo> _devices;

        public StubDeviceRegistry(IReadOnlyList<DeviceInfo> devices) => _devices = devices;

        public IReadOnlyList<DeviceInfo> AllDevices => _devices;

        public bool TryGetByIpPort(string configAddress, int port, [NotNullWhen(true)] out DeviceInfo? device)
        {
            device = _devices.FirstOrDefault(d =>
                string.Equals(d.ConfigAddress, configAddress, StringComparison.OrdinalIgnoreCase)
                && d.Port == port);
            return device is not null;
        }

        public bool TryGetDeviceByName(string deviceName, [NotNullWhen(true)] out DeviceInfo? device)
        {
            device = _devices.FirstOrDefault(d =>
                string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));
            return device is not null;
        }

        public Task<(IReadOnlySet<string> Added, IReadOnlySet<string> Removed)> ReloadAsync(List<DeviceOptions> devices)
            => Task.FromResult<(IReadOnlySet<string>, IReadOnlySet<string>)>(
                (new HashSet<string>(), new HashSet<string>()));
    }

    /// <summary>
    /// ISender that captures all Send&lt;TResponse&gt; calls for SnmpOidReceived messages.
    /// All other overloads (object-based, streaming) are no-ops.
    /// </summary>
    private sealed class CapturingSender : ISender
    {
        public List<SnmpOidReceived> Sent { get; } = new();

        Task<TResponse> ISender.Send<TResponse>(IRequest<TResponse> request, CancellationToken ct)
        {
            if (request is SnmpOidReceived msg)
                Sent.Add(msg);
            return Task.FromResult(default(TResponse)!);
        }

        Task ISender.Send<TRequest>(TRequest request, CancellationToken ct)
            => Task.CompletedTask;

        Task<object?> ISender.Send(object request, CancellationToken ct)
            => Task.FromResult<object?>(null);

        IAsyncEnumerable<TResponse> ISender.CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken ct)
            => EmptyAsyncEnumerable<TResponse>.Instance;

        IAsyncEnumerable<object?> ISender.CreateStream(object request, CancellationToken ct)
            => EmptyAsyncEnumerable<object?>.Instance;

        // Minimal helper to return an empty IAsyncEnumerable without a dependency on System.Linq.Async.
        private static class EmptyAsyncEnumerable<T>
        {
            public static readonly IAsyncEnumerable<T> Instance = new EmptyImpl();

            private sealed class EmptyImpl : IAsyncEnumerable<T>
            {
                public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken ct = default)
                    => new EmptyEnumerator();

                private sealed class EmptyEnumerator : IAsyncEnumerator<T>
                {
                    public T Current => default!;
                    public ValueTask<bool> MoveNextAsync() => new(false);
                    public ValueTask DisposeAsync() => default;
                }
            }
        }
    }

    /// <summary>ISnmpClient stub that returns a preconfigured response or throws an exception.
    /// Captures LastEndpoint and LastCommunity for assertion.</summary>
    private sealed class StubSnmpClient : ISnmpClient
    {
        public IList<Variable>? Response { get; set; }
        public Exception? ExceptionToThrow { get; set; }
        public IPEndPoint? LastEndpoint { get; private set; }
        public OctetString? LastCommunity { get; private set; }

        public Task<IList<Variable>> GetAsync(
            VersionCode version,
            IPEndPoint endpoint,
            OctetString community,
            IList<Variable> variables,
            CancellationToken ct)
        {
            LastEndpoint = endpoint;
            LastCommunity = community;
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;
            return Task.FromResult<IList<Variable>>(Response ?? new List<Variable>());
        }
    }

    /// <summary>
    /// Minimal IJobExecutionContext stub providing the properties MetricPollJob reads:
    /// MergedJobDataMap (ipAddress, port, pollIndex, intervalSeconds), JobDetail.Key.Name,
    /// and CancellationToken. All unused interface members throw NotImplementedException
    /// or return safe defaults.
    /// </summary>
    private sealed class StubJobExecutionContext : IJobExecutionContext
    {
        private readonly IJobDetail _jobDetail;

        public StubJobExecutionContext(
            string configAddress,
            int port,
            int pollIndex,
            int intervalSeconds,
            CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;

            MergedJobDataMap = new JobDataMap
            {
                ["configAddress"]   = configAddress,
                ["port"]            = port,
                ["pollIndex"]       = pollIndex,
                ["intervalSeconds"] = intervalSeconds
            };

            _jobDetail = JobBuilder.Create<MetricPollJob>()
                .WithIdentity($"metric-poll-{configAddress}_{port}-{pollIndex}")
                .Build();
        }

        public JobDataMap MergedJobDataMap { get; }
        public IJobDetail JobDetail        => _jobDetail;
        public CancellationToken CancellationToken { get; }
        public object? Result { get; set; }

        // --- Unused interface members: safe no-op or throw ---
        public IScheduler Scheduler                     => throw new NotImplementedException();
        public ITrigger Trigger                         => throw new NotImplementedException();
        public ICalendar? Calendar                      => throw new NotImplementedException();
        public bool Recovering                          => false;
        public TriggerKey RecoveringTriggerKey          => throw new NotImplementedException();
        public int RefireCount                          => 0;
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
