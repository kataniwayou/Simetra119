using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using Quartz.Impl;
using SnmpCollector.Lifecycle;
using SnmpCollector.Pipeline;
using System.Threading.Channels;
using Xunit;

namespace SnmpCollector.Tests.Lifecycle;

public sealed class GracefulShutdownServiceTests : IAsyncDisposable
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly StubTrapChannel _trapChannel;
    private readonly ServiceProvider _serviceProvider;
    private readonly GracefulShutdownService _service;

    public GracefulShutdownServiceTests()
    {
        _schedulerFactory = new StdSchedulerFactory();
        _trapChannel = new StubTrapChannel();

        var services = new ServiceCollection();
        _serviceProvider = services.BuildServiceProvider();

        _service = new GracefulShutdownService(
            _schedulerFactory,
            _trapChannel,
            _serviceProvider,
            NullLogger<GracefulShutdownService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        _serviceProvider.Dispose();
        var scheduler = await _schedulerFactory.GetScheduler();
        if (scheduler.IsStarted)
            await scheduler.Shutdown();
    }

    [Fact]
    public async Task StartAsync_CompletesImmediately()
    {
        await _service.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_CompletesWithinTimeout()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _service.StopAsync(cts.Token);
    }

    [Fact]
    public async Task StopAsync_CallsComplete()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.Start();

        await _service.StopAsync(CancellationToken.None);

        Assert.True(_trapChannel.CompleteCalled);
    }

    [Fact]
    public async Task StopAsync_CallsWaitForDrainAsync()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.Start();

        await _service.StopAsync(CancellationToken.None);

        Assert.True(_trapChannel.WaitForDrainCalled);
    }

    [Fact]
    public async Task StopAsync_PutsSchedulerInStandby()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.Start();

        await _service.StopAsync(CancellationToken.None);

        Assert.True(scheduler.InStandbyMode);
    }

    [Fact]
    public async Task StopAsync_HandlesNoLeaseService()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.Start();

        await _service.StopAsync(CancellationToken.None);
    }

    private sealed class StubTrapChannel : ITrapChannel
    {
        public bool CompleteCalled { get; private set; }
        public bool WaitForDrainCalled { get; private set; }

        private readonly Channel<VarbindEnvelope> _channel = Channel.CreateUnbounded<VarbindEnvelope>();

        public ChannelWriter<VarbindEnvelope> Writer => _channel.Writer;
        public ChannelReader<VarbindEnvelope> Reader => _channel.Reader;

        public void Complete()
        {
            CompleteCalled = true;
            _channel.Writer.TryComplete();
        }

        public Task WaitForDrainAsync(CancellationToken cancellationToken)
        {
            WaitForDrainCalled = true;
            return Task.CompletedTask;
        }
    }
}
