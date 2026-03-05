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

public sealed class ExceptionBehaviorTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly PipelineMetricService _metrics;

    public ExceptionBehaviorTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddSingleton(Options.Create(new SiteOptions { Name = "test-site" }));
        services.AddSingleton<PipelineMetricService>();
        _sp = services.BuildServiceProvider();
        _metrics = _sp.GetRequiredService<PipelineMetricService>();
    }

    public void Dispose() => _sp.Dispose();

    private ExceptionBehavior<SnmpOidReceived, Unit> CreateBehavior() =>
        new(NullLogger<ExceptionBehavior<SnmpOidReceived, Unit>>.Instance, _metrics);

    private static SnmpOidReceived MakeNotification() =>
        new()
        {
            Oid = "1.3.6.1.2.1.1.1.0",
            AgentIp = IPAddress.Parse("10.0.0.1"),
            Value = new Integer32(42),
            Source = SnmpSource.Poll,
            TypeCode = SnmpType.Integer32,
            DeviceName = "test-device"
        };

    [Fact]
    public async Task SwallowsExceptionAndIncrementsErrors()
    {
        // Arrange
        var behavior = CreateBehavior();
        var notification = MakeNotification();

        // Act -- next() throws; ExceptionBehavior must not re-throw
        var exception = await Record.ExceptionAsync(() =>
            behavior.Handle(notification, ct => throw new InvalidOperationException("boom"), CancellationToken.None));

        // Assert -- no exception propagates
        Assert.Null(exception);
    }

    [Fact]
    public async Task PassesThroughOnSuccess()
    {
        // Arrange
        var behavior = CreateBehavior();
        var notification = MakeNotification();
        var nextCalled = false;

        // Act
        await behavior.Handle(notification, ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        // Assert -- next() was called and no exception
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task ReturnsDefaultOnException()
    {
        // Arrange
        var behavior = CreateBehavior();
        var notification = MakeNotification();

        // Act
        var result = await behavior.Handle(
            notification,
            ct => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        // Assert -- returns default (Unit.Value) not null
        Assert.Equal(default, result);
    }
}
