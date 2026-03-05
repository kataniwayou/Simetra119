using System.Diagnostics.CodeAnalysis;
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

public sealed class ValidationBehaviorTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly PipelineMetricService _metrics;

    public ValidationBehaviorTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddSingleton(Options.Create(new SiteOptions { Name = "test-site" }));
        services.AddSingleton<PipelineMetricService>();
        _sp = services.BuildServiceProvider();
        _metrics = _sp.GetRequiredService<PipelineMetricService>();
    }

    public void Dispose() => _sp.Dispose();

    private ValidationBehavior<SnmpOidReceived, Unit> CreateBehavior(IDeviceRegistry? registry = null) =>
        new(
            NullLogger<ValidationBehavior<SnmpOidReceived, Unit>>.Instance,
            _metrics,
            registry ?? new StubDeviceRegistry(knownIp: null));

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
    public async Task AcceptsValidOid()
    {
        // DeviceName already set (poll path) -- registry not consulted
        var behavior = CreateBehavior();
        var nextCalled = false;

        await behavior.Handle(MakeNotification("1.3.6.1.2.1.25.3.3.1.2", deviceName: "known-device"), ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.True(nextCalled);
    }

    // --- Device resolution tests ---

    [Fact]
    public async Task RejectsUnknownDevice()
    {
        // DeviceName is null (trap path); IP 10.99.99.99 not in registry
        var registry = new StubDeviceRegistry(knownIp: "10.0.0.1");
        var behavior = CreateBehavior(registry);
        var nextCalled = false;

        await behavior.Handle(MakeNotification("1.3.6.1.2.1.1.1.0", agentIp: "10.99.99.99", deviceName: null), ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.False(nextCalled);
    }

    [Fact]
    public async Task AcceptsKnownDevice_PopulatesDeviceName()
    {
        // DeviceName is null (trap path); IP 10.0.0.1 IS in registry -> DeviceName populated
        var registry = new StubDeviceRegistry(knownIp: "10.0.0.1");
        var behavior = CreateBehavior(registry);
        var notification = MakeNotification("1.3.6.1.2.1.1.1.0", agentIp: "10.0.0.1", deviceName: null);
        var nextCalled = false;

        await behavior.Handle(notification, ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.True(nextCalled);
        // DeviceName enriched in-place by ValidationBehavior
        Assert.Equal("stub-device", notification.DeviceName);
    }

    [Fact]
    public async Task SkipsRegistryWhenDeviceNameAlreadySet()
    {
        // Poll path: DeviceName is pre-set, registry TryGetDevice should NOT be consulted
        var registry = new StubDeviceRegistry(knownIp: null); // registry knows no devices
        var behavior = CreateBehavior(registry);
        var nextCalled = false;

        await behavior.Handle(MakeNotification("1.3.6.1.2.1.1.1.0", deviceName: "pre-set-device"), ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        // Even though registry has no devices, DeviceName was pre-set so it passes
        Assert.True(nextCalled);
    }

    // --- Stub IDeviceRegistry ---

    private sealed class StubDeviceRegistry : IDeviceRegistry
    {
        private readonly IPAddress? _knownIp;

        public StubDeviceRegistry(string? knownIp)
        {
            _knownIp = knownIp is not null ? IPAddress.Parse(knownIp) : null;
        }

        public bool TryGetDevice(IPAddress senderIp, [NotNullWhen(true)] out DeviceInfo? device)
        {
            if (_knownIp is not null && senderIp.MapToIPv4().Equals(_knownIp.MapToIPv4()))
            {
                device = new DeviceInfo("stub-device", senderIp.ToString(), "public", []);
                return true;
            }
            device = null;
            return false;
        }

        public bool TryGetDeviceByName(string deviceName, [NotNullWhen(true)] out DeviceInfo? device)
        {
            device = null;
            return false;
        }

        public IReadOnlyList<DeviceInfo> AllDevices => [];
    }
}
