using System.Net;
using Lextm.SharpSnmpLib;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using SnmpCollector.Pipeline.Behaviors;
using SnmpCollector.Telemetry;
using Xunit;

namespace SnmpCollector.Tests.Pipeline.Behaviors;

public sealed class LoggingBehaviorTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly PipelineMetricService _metrics;

    public LoggingBehaviorTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddSingleton(Options.Create(new SiteOptions { Name = "test-site" }));
        services.AddSingleton<PipelineMetricService>();
        _sp = services.BuildServiceProvider();
        _metrics = _sp.GetRequiredService<PipelineMetricService>();
    }

    public void Dispose() => _sp.Dispose();

    private static SnmpOidReceived MakeNotification(string oid = "1.3.6.1.2.1.1.1.0") =>
        new()
        {
            Oid = oid,
            AgentIp = IPAddress.Parse("10.0.0.1"),
            Value = new Integer32(42),
            Source = SnmpSource.Poll,
            TypeCode = SnmpType.Integer32,
            DeviceName = "test-device"
        };

    private LoggingBehavior<SnmpOidReceived, Unit> CreateBehavior() =>
        new(NullLogger<LoggingBehavior<SnmpOidReceived, Unit>>.Instance, _metrics);

    [Fact]
    public async Task AlwaysCallsNext()
    {
        var behavior = CreateBehavior();
        var notification = MakeNotification();
        var nextCalled = false;

        await behavior.Handle(notification, ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task LogsDebugForSnmpOidReceived_NoException()
    {
        // NullLogger swallows all log calls -- confirms no crash when logging fires
        var behavior = CreateBehavior();
        var notification = MakeNotification();

        var exception = await Record.ExceptionAsync(() =>
            behavior.Handle(notification, ct => Task.FromResult(Unit.Value), CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task PassesThroughNonSnmpNotification()
    {
        // LoggingBehavior uses open generic: must also work for non-SnmpOidReceived types.
        var behavior = new LoggingBehavior<StubNotification, Unit>(
            NullLogger<LoggingBehavior<StubNotification, Unit>>.Instance, _metrics);

        var nextCalled = false;

        await behavior.Handle(new StubNotification(), ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.True(nextCalled);
    }

    private sealed class StubNotification : INotification { }
}
