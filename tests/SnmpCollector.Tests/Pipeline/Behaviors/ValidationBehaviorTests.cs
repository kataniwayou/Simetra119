using System.Net;
using Lextm.SharpSnmpLib;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SnmpCollector.Pipeline;
using SnmpCollector.Pipeline.Behaviors;
using SnmpCollector.Telemetry;
using Xunit;

namespace SnmpCollector.Tests.Pipeline.Behaviors;

public sealed class ValidationBehaviorTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly PipelineMetricService _metrics;

    public ValidationBehaviorTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddSingleton<PipelineMetricService>();
        _sp = services.BuildServiceProvider();
        _metrics = _sp.GetRequiredService<PipelineMetricService>();
    }

    public void Dispose() => _sp.Dispose();

    private ValidationBehavior<SnmpOidReceived, Unit> CreateBehavior() =>
        new(
            NullLogger<ValidationBehavior<SnmpOidReceived, Unit>>.Instance,
            _metrics);

    private static SnmpOidReceived MakeNotification(string oid, string agentIp = "10.0.0.1", string? deviceName = null) =>
        new()
        {
            Oid = oid,
            AgentIp = IPAddress.Parse(agentIp),
            Value = new Integer32(42),
            Source = SnmpSource.Trap,
            TypeCode = SnmpType.Integer32,
            DeviceName = deviceName
        };

    // --- OID format validation tests ---

    [Fact]
    public async Task RejectsInvalidOidFormat_NoDotsInOid()
    {
        var behavior = CreateBehavior();
        var nextCalled = false;

        await behavior.Handle(MakeNotification("1234"), ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.False(nextCalled);
    }

    [Fact]
    public async Task RejectsInvalidOidFormat_SingleArc()
    {
        var behavior = CreateBehavior();
        var nextCalled = false;

        await behavior.Handle(MakeNotification("1"), ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.False(nextCalled);
    }

    [Fact]
    public async Task RejectsInvalidOidFormat_NonNumeric()
    {
        var behavior = CreateBehavior();
        var nextCalled = false;

        await behavior.Handle(MakeNotification("1.3.abc.5"), ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.False(nextCalled);
    }

    [Fact]
    public async Task AcceptsValidOid_WhenDeviceNameSet()
    {
        // DeviceName already set (poll path) -- passes validation
        var behavior = CreateBehavior();
        var nextCalled = false;

        await behavior.Handle(MakeNotification("1.3.6.1.2.1.25.3.3.1.2", deviceName: "known-device"), ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.True(nextCalled);
    }

    // --- DeviceName validation tests ---

    [Fact]
    public async Task RejectsMissingDeviceName()
    {
        // DeviceName is null -> rejected with MissingDeviceName reason
        var behavior = CreateBehavior();
        var nextCalled = false;

        await behavior.Handle(MakeNotification("1.3.6.1.2.1.1.1.0", deviceName: null), ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.False(nextCalled);
    }

    [Fact]
    public async Task AcceptsPreSetDeviceName()
    {
        // Poll path: DeviceName is pre-set, passes validation
        var behavior = CreateBehavior();
        var nextCalled = false;

        await behavior.Handle(MakeNotification("1.3.6.1.2.1.1.1.0", deviceName: "pre-set-device"), ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.True(nextCalled);
    }
}
