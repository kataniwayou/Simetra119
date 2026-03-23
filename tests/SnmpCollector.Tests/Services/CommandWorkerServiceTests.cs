using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Net;
using System.Threading.Channels;
using Lextm.SharpSnmpLib;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using SnmpCollector.Services;
using SnmpCollector.Telemetry;
using SnmpCollector.Tests.Helpers;
using Xunit;

namespace SnmpCollector.Tests.Services;

/// <summary>
/// Unit tests for <see cref="CommandWorkerService"/> verifying SET execution, ISender.Send dispatch,
/// Source=Command enforcement, DeviceName resolution from DeviceRegistry, MetricName pre-set,
/// error isolation, and counter increments.
/// </summary>
[Collection(NonParallelCollection.Name)]
public sealed class CommandWorkerServiceTests : IDisposable
{
    private const string TestDeviceName = "request-device";
    private const string TestIp = "10.0.0.1";
    private const int TestPort = 161;
    private const string TestCommandName = "set-power-threshold";
    private const string TestOid = "1.3.6.1.4.1.9999.1.1.0";
    private const string TestValue = "42";
    private const string TestValueType = "Integer32";
    private const string RegistryDeviceName = "registry-device";

    private readonly ServiceProvider _sp;
    private readonly PipelineMetricService _metrics;
    private readonly MeterListener _meterListener;
    private readonly List<(string InstrumentName, long Value, KeyValuePair<string, object?>[] Tags)> _measurements = new();

    public CommandWorkerServiceTests()
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

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static CommandRequest MakeRequest(
        string commandName = TestCommandName,
        string ip = TestIp,
        int port = TestPort,
        string value = TestValue,
        string valueType = TestValueType,
        string tenantId = "test-tenant",
        int priority = 1)
        => new(ip, port, commandName, value, valueType, tenantId, priority);

    private CommandWorkerService CreateService(
        ISender sender,
        ICommandChannel commandChannel,
        ISnmpClient? snmpClient = null,
        IDeviceRegistry? deviceRegistry = null,
        ICommandMapService? commandMapService = null,
        ILeaderElection? leaderElection = null,
        ILogger<CommandWorkerService>? logger = null)
    {
        return new CommandWorkerService(
            commandChannel,
            snmpClient ?? new StubSnmpClient(),
            sender,
            deviceRegistry ?? new StubDeviceRegistry(),
            commandMapService ?? new StubCommandMapService(),
            new RotatingCorrelationService(),
            leaderElection ?? new AlwaysLeaderElection(),
            _metrics,
            Options.Create(new SnapshotJobOptions()),
            logger ?? NullLogger<CommandWorkerService>.Instance);
    }

    private static PrimedCommandChannel CreateCommandChannel(IEnumerable<CommandRequest> requests)
    {
        var channel = new PrimedCommandChannel();
        foreach (var r in requests)
            channel.Write(r);
        channel.Complete();
        return channel;
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    // -----------------------------------------------------------------------
    // 1. DispatchesSetAndCallsSenderSend
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DispatchesSetAndCallsSenderSend()
    {
        var sender = new CapturingSender();
        var channel = CreateCommandChannel([MakeRequest()]);
        var service = CreateService(sender, channel);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => sender.Calls.Count >= 1, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Single(sender.Calls);
        Assert.Equal(TestOid, sender.Calls[0].Oid);
    }

    // -----------------------------------------------------------------------
    // 2. SetsSourceToCommand
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SetsSourceToCommand()
    {
        var sender = new CapturingSender();
        var channel = CreateCommandChannel([MakeRequest()]);
        var service = CreateService(sender, channel);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => sender.Calls.Count >= 1, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Single(sender.Calls);
        Assert.Equal(SnmpSource.Command, sender.Calls[0].Source);
    }

    // -----------------------------------------------------------------------
    // 3. SetsDeviceNameFromCommandRequest
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SetsDeviceNameFromDeviceRegistry()
    {
        var sender = new CapturingSender();
        var channel = CreateCommandChannel([MakeRequest()]);
        var registry = new StubDeviceRegistry(deviceName: "registry-name");
        var service = CreateService(sender, channel, deviceRegistry: registry);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => sender.Calls.Count >= 1, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Single(sender.Calls);
        Assert.Equal("registry-name", sender.Calls[0].DeviceName);
    }

    // -----------------------------------------------------------------------
    // 4. PreSetsMetricNameWhenOidFoundInCommandMap
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PreSetsMetricNameWhenOidFoundInCommandMap()
    {
        var sender = new CapturingSender();
        var channel = CreateCommandChannel([MakeRequest()]);
        var cmdMap = new StubCommandMapService(resolveCommandName: "set-power-threshold");
        var service = CreateService(sender, channel, commandMapService: cmdMap);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => sender.Calls.Count >= 1, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Single(sender.Calls);
        Assert.Equal("set-power-threshold", sender.Calls[0].MetricName);
    }

    // -----------------------------------------------------------------------
    // 5. LeavesMetricNameNullWhenOidNotInCommandMap
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LeavesMetricNameNullWhenOidNotInCommandMap()
    {
        var sender = new CapturingSender();
        var channel = CreateCommandChannel([MakeRequest()]);
        var cmdMap = new StubCommandMapService(resolveCommandName: null);
        var service = CreateService(sender, channel, commandMapService: cmdMap);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => sender.Calls.Count >= 1, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Single(sender.Calls);
        Assert.Null(sender.Calls[0].MetricName);
    }

    // -----------------------------------------------------------------------
    // 6. ExceptionInSetAsync_ContinuesProcessing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExceptionInSetAsync_ContinuesProcessing()
    {
        var sender = new CapturingSender();
        var channel = CreateCommandChannel([MakeRequest(), MakeRequest()]);
        var snmpClient = new FaultingSnmpClient(throwOnCallNumber: 1);
        var service = CreateService(sender, channel, snmpClient: snmpClient);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => snmpClient.CallCount >= 2, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        // First request threw, second succeeded
        Assert.Equal(2, snmpClient.CallCount);
        Assert.Single(sender.Calls);
    }

    // -----------------------------------------------------------------------
    // 7. OidNotInCommandMap_IncrementsFailedAndSkips
    // -----------------------------------------------------------------------

    [Fact]
    public async Task OidNotInCommandMap_IncrementsFailedAndSkips()
    {
        var sender = new CapturingSender();
        var channel = CreateCommandChannel([MakeRequest()]);
        var cmdMap = new StubCommandMapService(resolveCommandOid: null);
        var service = CreateService(sender, channel, commandMapService: cmdMap);

        await service.StartAsync(CancellationToken.None);
        // Wait for the channel to drain (channel is completed, so worker will finish quickly)
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        Assert.Empty(sender.Calls);
        var failedCount = _measurements.Count(m => m.InstrumentName == "snmp.command.failed");
        Assert.True(failedCount >= 1);
    }

    // -----------------------------------------------------------------------
    // 8. DeviceNotFound_IncrementsFailedAndSkips
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeviceNotFound_IncrementsFailedAndSkips()
    {
        var sender = new CapturingSender();
        var channel = CreateCommandChannel([MakeRequest()]);
        var registry = new StubDeviceRegistry(returnsDevice: false);
        var service = CreateService(sender, channel, deviceRegistry: registry);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        Assert.Empty(sender.Calls);
        var failedCount = _measurements.Count(m => m.InstrumentName == "snmp.command.failed");
        Assert.True(failedCount >= 1);
    }

    // -----------------------------------------------------------------------
    // 9. NoCommandDispatchedCounter (dispatch counted in SnapshotJob, not here)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NoCommandDispatchedCounter_OnSuccess()
    {
        var sender = new CapturingSender();
        var channel = CreateCommandChannel([MakeRequest()]);
        var service = CreateService(sender, channel);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => sender.Calls.Count >= 1, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        var dispatchedCount = _measurements.Count(m => m.InstrumentName == "snmp.command.dispatched");
        Assert.Equal(0, dispatchedCount); // dispatch counter moved to SnapshotJob
    }

    // -----------------------------------------------------------------------
    // 10. Successful_SET_logs_Information_with_duration
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Successful_SET_logs_Information_with_duration()
    {
        var sender = new CapturingSender();
        var channel = CreateCommandChannel([MakeRequest()]);
        var logger = new CapturingLogger();
        var service = CreateService(sender, channel, logger: logger);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => sender.Calls.Count >= 1, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        var infoLogs = logger.Entries
            .Where(e => e.Level == LogLevel.Information && e.Message.Contains("completed for"))
            .ToList();
        Assert.Single(infoLogs);
        Assert.Contains(TestCommandName, infoLogs[0].Message);
        Assert.Contains(RegistryDeviceName, infoLogs[0].Message);
        Assert.Contains("ms", infoLogs[0].Message);
    }

    // -----------------------------------------------------------------------
    // 11. Failed_SET_logs_Warning_with_duration
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Failed_SET_logs_Warning_with_duration()
    {
        var sender = new CapturingSender();
        var channel = CreateCommandChannel([MakeRequest()]);
        var snmpClient = new FaultingSnmpClient(throwOnCallNumber: 1);
        var logger = new CapturingLogger();
        var service = CreateService(sender, channel, snmpClient: snmpClient, logger: logger);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => snmpClient.CallCount >= 1, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        var warningLogs = logger.Entries
            .Where(e => e.Level == LogLevel.Warning && e.Message.Contains(TestCommandName))
            .ToList();
        Assert.Single(warningLogs);
        Assert.Contains($"{TestIp}:{TestPort}", warningLogs[0].Message);
    }

    [Fact]
    public async Task NonLeader_skips_SET_without_calling_SetAsync()
    {
        var sender = new CapturingSender();
        var channel = CreateCommandChannel([MakeRequest()]);
        var snmpClient = new StubSnmpClient();
        var service = CreateService(sender, channel, snmpClient: snmpClient, leaderElection: new NeverLeaderElection());

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(200); // give drain loop time to process
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, snmpClient.SetCallCount);
        Assert.Empty(sender.Calls);
    }

    // -----------------------------------------------------------------------
    // Stubs
    // -----------------------------------------------------------------------

    /// <summary>ISender that records all Send calls to a thread-safe list.</summary>
    private sealed class CapturingSender : ISender
    {
        private readonly List<SnmpOidReceived> _calls = new();
        private readonly object _lock = new();

        public IReadOnlyList<SnmpOidReceived> Calls
        {
            get { lock (_lock) return _calls.ToList(); }
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is SnmpOidReceived msg)
                lock (_lock) { _calls.Add(msg); }
            return Task.FromResult(default(TResponse)!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            if (request is SnmpOidReceived msg)
                lock (_lock) { _calls.Add(msg); }
            return Task.FromResult<object?>(null);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => EmptyAsyncEnumerable<TResponse>.Instance;

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => EmptyAsyncEnumerable<object?>.Instance;
    }

    /// <summary>Command channel backed by pre-loaded, pre-completed channel for testing.</summary>
    private sealed class PrimedCommandChannel : ICommandChannel
    {
        private readonly Channel<CommandRequest> _channel = Channel.CreateUnbounded<CommandRequest>();

        public void Write(CommandRequest request) => _channel.Writer.TryWrite(request);
        public void Complete() => _channel.Writer.TryComplete();

        public ChannelWriter<CommandRequest> Writer => _channel.Writer;
        public ChannelReader<CommandRequest> Reader => _channel.Reader;
    }

    /// <summary>ISnmpClient stub with configurable SetAsync response.</summary>
    private sealed class StubSnmpClient : ISnmpClient
    {
        private readonly IList<Variable> _setResponse;
        private int _setCallCount;

        public int SetCallCount => Volatile.Read(ref _setCallCount);

        public StubSnmpClient(IList<Variable>? setResponse = null)
        {
            _setResponse = setResponse ?? new List<Variable>
            {
                new Variable(new ObjectIdentifier(TestOid), new Integer32(42))
            };
        }

        public Task<IList<Variable>> SetAsync(
            VersionCode version, IPEndPoint endpoint, OctetString community,
            Variable variable, CancellationToken ct)
        {
            Interlocked.Increment(ref _setCallCount);
            return Task.FromResult(_setResponse);
        }

        public Task<IList<Variable>> GetAsync(
            VersionCode version, IPEndPoint endpoint, OctetString community,
            IList<Variable> variables, CancellationToken ct)
            => throw new NotImplementedException();
    }

    /// <summary>ISnmpClient that throws on a specific call number.</summary>
    private sealed class FaultingSnmpClient : ISnmpClient
    {
        private readonly int _throwOnCallNumber;
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public FaultingSnmpClient(int throwOnCallNumber)
            => _throwOnCallNumber = throwOnCallNumber;

        public Task<IList<Variable>> SetAsync(
            VersionCode version, IPEndPoint endpoint, OctetString community,
            Variable variable, CancellationToken ct)
        {
            var count = Interlocked.Increment(ref _callCount);
            if (count == _throwOnCallNumber)
                throw new InvalidOperationException("Simulated SET failure");
            return Task.FromResult<IList<Variable>>(new List<Variable>
            {
                new Variable(new ObjectIdentifier(TestOid), new Integer32(42))
            });
        }

        public Task<IList<Variable>> GetAsync(
            VersionCode version, IPEndPoint endpoint, OctetString community,
            IList<Variable> variables, CancellationToken ct)
            => throw new NotImplementedException();
    }

    /// <summary>IDeviceRegistry stub that returns a pre-configured device.</summary>
    private sealed class StubDeviceRegistry : IDeviceRegistry
    {
        private readonly bool _returnsDevice;
        private readonly DeviceInfo _device;

        public StubDeviceRegistry(bool returnsDevice = true, string deviceName = RegistryDeviceName)
        {
            _returnsDevice = returnsDevice;
            _device = new DeviceInfo(
                Name: deviceName,
                ConfigAddress: TestIp,
                ResolvedIp: TestIp,
                Port: TestPort,
                PollGroups: Array.Empty<MetricPollInfo>(),
                CommunityString: "public");
        }

        public bool TryGetByIpPort(string configAddress, int port, [NotNullWhen(true)] out DeviceInfo? device)
        {
            device = _returnsDevice ? _device : null;
            return _returnsDevice;
        }

        public bool TryGetDeviceByName(string deviceName, [NotNullWhen(true)] out DeviceInfo? device)
            => throw new NotImplementedException();

        public IReadOnlyList<DeviceInfo> AllDevices => throw new NotImplementedException();

        public Task<(IReadOnlySet<string> Added, IReadOnlySet<string> Removed)> ReloadAsync(List<DeviceInfo> devices)
            => throw new NotImplementedException();
    }

    /// <summary>ICommandMapService stub with configurable resolution.</summary>
    private sealed class StubCommandMapService : ICommandMapService
    {
        private readonly string? _resolveCommandOid;
        private readonly string? _resolveCommandName;

        public StubCommandMapService(
            string? resolveCommandOid = TestOid,
            string? resolveCommandName = TestCommandName)
        {
            _resolveCommandOid = resolveCommandOid;
            _resolveCommandName = resolveCommandName;
        }

        public string? ResolveCommandOid(string commandName) => _resolveCommandOid;
        public string? ResolveCommandName(string oid) => _resolveCommandName;
        public IReadOnlyCollection<string> GetAllCommandNames() => throw new NotImplementedException();
        public bool Contains(string commandName) => throw new NotImplementedException();
        public int Count => throw new NotImplementedException();
        public void UpdateMap(Dictionary<string, string> entries) => throw new NotImplementedException();
    }

    /// <summary>Provides empty async enumerables without requiring System.Linq.Async.</summary>
    private sealed class EmptyAsyncEnumerable<T> : IAsyncEnumerable<T>, IAsyncEnumerator<T>
    {
        public static readonly EmptyAsyncEnumerable<T> Instance = new();
        public T Current => default!;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(false);
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;
    }

    /// <summary>Logger that captures log entries for assertion in duration-logging tests.</summary>
    private sealed class CapturingLogger : ILogger<CommandWorkerService>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = new();
        private readonly object _lock = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get { lock (_lock) return _entries.ToList(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_lock) { _entries.Add((logLevel, formatter(state, exception))); }
        }
    }

    private sealed class NeverLeaderElection : ILeaderElection
    {
        public bool IsLeader => false;
        public string CurrentRole => "follower";
    }
}
