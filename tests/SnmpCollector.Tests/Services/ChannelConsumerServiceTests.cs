using System.Diagnostics.Metrics;
using System.Net;
using System.Threading.Channels;
using Lextm.SharpSnmpLib;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
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
/// Unit tests for <see cref="ChannelConsumerService"/> verifying ISender.Send dispatch,
/// Source=Trap enforcement, DeviceName propagation, counter increments, and exception resilience.
///
/// Placed in NonParallelMeterTests collection to prevent cross-test meter contamination
/// (MeterListener is a global listener; parallel tests with the same meter name interfere).
/// </summary>
[Collection(NonParallelCollection.Name)]
public sealed class ChannelConsumerServiceTests : IDisposable
{
    private const string DeviceName = "test-switch";
    private static readonly IPAddress DeviceIp = IPAddress.Parse("10.0.2.1");

    private readonly ServiceProvider _sp;
    private readonly PipelineMetricService _metrics;
    private readonly MeterListener _meterListener;
    private readonly List<(string InstrumentName, long Value, KeyValuePair<string, object?>[] Tags)> _measurements = new();

    public ChannelConsumerServiceTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        _sp = services.BuildServiceProvider();

        _metrics = new PipelineMetricService(
            _sp.GetRequiredService<IMeterFactory>(),
            Options.Create(new SiteOptions { Name = "test-site" }));

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

    private static VarbindEnvelope MakeEnvelope(string oid = "1.3.6.1.2.1.1.1.0")
        => new VarbindEnvelope(
            Oid: oid,
            Value: new Integer32(99),
            TypeCode: SnmpType.Integer32,
            AgentIp: DeviceIp,
            DeviceName: DeviceName);

    private ChannelConsumerService CreateService(ISender sender, IDeviceChannelManager channelManager)
        => new ChannelConsumerService(
            channelManager,
            sender,
            new RotatingCorrelationService(),
            _metrics,
            NullLogger<ChannelConsumerService>.Instance);

    /// <summary>Creates a channel pre-loaded with envelopes, then completed so ReadAllAsync finishes.</summary>
    private static PrimedChannelManager CreateChannelManager(IEnumerable<VarbindEnvelope> envelopes)
    {
        var manager = new PrimedChannelManager(DeviceName);
        foreach (var e in envelopes)
            manager.Write(e);
        manager.Complete();
        return manager;
    }

    /// <summary>Wait for a condition to become true with a timeout, polling every 10ms.</summary>
    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    // -----------------------------------------------------------------------
    // 1. ConsumesVarbindAndCallsSenderSend
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConsumesVarbindAndCallsSenderSend()
    {
        var sender = new CapturingSender();
        var manager = CreateChannelManager([MakeEnvelope()]);
        var service = CreateService(sender, manager);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => sender.Calls.Count >= 1, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Single(sender.Calls);
    }

    // -----------------------------------------------------------------------
    // 2. SetsSourceToTrap
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SetsSourceToTrap()
    {
        var sender = new CapturingSender();
        var manager = CreateChannelManager([MakeEnvelope()]);
        var service = CreateService(sender, manager);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => sender.Calls.Count >= 1, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Single(sender.Calls);
        Assert.Equal(SnmpSource.Trap, sender.Calls[0].Source);
    }

    // -----------------------------------------------------------------------
    // 3. SetsDeviceNameFromEnvelope
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SetsDeviceNameFromEnvelope()
    {
        var sender = new CapturingSender();
        var manager = CreateChannelManager([MakeEnvelope()]);
        var service = CreateService(sender, manager);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => sender.Calls.Count >= 1, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Single(sender.Calls);
        Assert.Equal(DeviceName, sender.Calls[0].DeviceName);
    }

    // -----------------------------------------------------------------------
    // 4. IncrementsTrapReceived
    // -----------------------------------------------------------------------

    [Fact]
    public async Task IncrementsTrapReceived()
    {
        var sender = new CapturingSender();
        var manager = CreateChannelManager([MakeEnvelope(), MakeEnvelope()]);
        var service = CreateService(sender, manager);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => sender.Calls.Count >= 2, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        // Count only OUR meter's snmp.trap.received measurements
        var receivedCount = _measurements.Count(m => m.InstrumentName == "snmp.trap.received");
        Assert.Equal(2, receivedCount);
    }

    // -----------------------------------------------------------------------
    // 5. ExceptionInSend_ContinuesProcessing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExceptionInSend_ContinuesProcessing()
    {
        // Sender throws on first call, succeeds on second
        var sender = new FaultingThenSucceedingSender(throwOnFirstCall: true);
        var manager = CreateChannelManager([MakeEnvelope("1.3.6.1.1"), MakeEnvelope("1.3.6.1.2")]);
        var service = CreateService(sender, manager);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => sender.TotalAttempts >= 2, TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        // Both envelopes were attempted: first threw, second succeeded
        Assert.Equal(2, sender.TotalAttempts);
        Assert.Equal(1, sender.SuccessCount);
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
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<object?>();
    }

    /// <summary>ISender that throws on the first call, then succeeds.</summary>
    private sealed class FaultingThenSucceedingSender : ISender
    {
        private int _callCount;
        private readonly bool _throwOnFirstCall;

        public int TotalAttempts => Volatile.Read(ref _callCount);
        public int SuccessCount { get; private set; }

        public FaultingThenSucceedingSender(bool throwOnFirstCall) => _throwOnFirstCall = throwOnFirstCall;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var count = Interlocked.Increment(ref _callCount);
            if (_throwOnFirstCall && count == 1)
                throw new InvalidOperationException("Simulated Send failure");
            SuccessCount++;
            return Task.FromResult(default(TResponse)!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            var count = Interlocked.Increment(ref _callCount);
            if (_throwOnFirstCall && count == 1)
                throw new InvalidOperationException("Simulated Send failure");
            SuccessCount++;
            return Task.FromResult<object?>(null);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<object?>();
    }

    /// <summary>Channel manager backed by pre-loaded, pre-completed channels for testing.</summary>
    private sealed class PrimedChannelManager : IDeviceChannelManager
    {
        private readonly string _deviceName;
        private readonly Channel<VarbindEnvelope> _channel;

        public PrimedChannelManager(string deviceName)
        {
            _deviceName = deviceName;
            _channel = Channel.CreateUnbounded<VarbindEnvelope>();
        }

        public void Write(VarbindEnvelope envelope) => _channel.Writer.TryWrite(envelope);
        public void Complete() => _channel.Writer.Complete();

        public ChannelWriter<VarbindEnvelope> GetWriter(string name) => _channel.Writer;
        public ChannelReader<VarbindEnvelope> GetReader(string name) => _channel.Reader;
        public IReadOnlyCollection<string> DeviceNames => [_deviceName];
        public void CompleteAll() => _channel.Writer.TryComplete();

        public async Task WaitForDrainAsync(CancellationToken cancellationToken)
        {
            await _channel.Reader.Completion.WaitAsync(cancellationToken);
        }
    }

    /// <summary>Provides empty async enumerables without requiring System.Linq.Async.</summary>
    private static class AsyncEnumerable
    {
        public static IAsyncEnumerable<T> Empty<T>() => EmptyAsyncEnumerable<T>.Instance;

        private sealed class EmptyAsyncEnumerable<T> : IAsyncEnumerable<T>, IAsyncEnumerator<T>
        {
            public static readonly EmptyAsyncEnumerable<T> Instance = new();
            public T Current => default!;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(false);
            public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;
        }
    }
}
