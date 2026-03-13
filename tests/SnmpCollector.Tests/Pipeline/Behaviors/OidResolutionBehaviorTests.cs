using System.Net;
using Lextm.SharpSnmpLib;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using SnmpCollector.Pipeline.Behaviors;
using Xunit;

namespace SnmpCollector.Tests.Pipeline.Behaviors;

public sealed class OidResolutionBehaviorTests
{
    private static SnmpOidReceived MakeNotification(string oid) =>
        new()
        {
            Oid = oid,
            AgentIp = IPAddress.Parse("10.0.0.1"),
            Value = new Integer32(42),
            Source = SnmpSource.Poll,
            TypeCode = SnmpType.Integer32,
            DeviceName = "test-device"
        };

    [Fact]
    public async Task SetsMetricNameFromOidMap()
    {
        // Arrange: OID is in the map -> MetricName populated
        var oidMapService = new StubOidMapService(knownOid: "1.3.6.1.2.1.25.3.3.1.2", metricName: "hrProcessorLoad");
        var behavior = new OidResolutionBehavior<SnmpOidReceived, Unit>(oidMapService, NullLogger<OidResolutionBehavior<SnmpOidReceived, Unit>>.Instance);
        var notification = MakeNotification("1.3.6.1.2.1.25.3.3.1.2");

        await behavior.Handle(notification, ct => Task.FromResult(Unit.Value), CancellationToken.None);

        Assert.Equal("hrProcessorLoad", notification.MetricName);
    }

    [Fact]
    public async Task SetsUnknownForAbsentOid()
    {
        // Arrange: OID not in map -> MetricName set to "Unknown"
        var oidMapService = new StubOidMapService(knownOid: "1.3.6.1.2.1.25.3.3.1.2", metricName: "hrProcessorLoad");
        var behavior = new OidResolutionBehavior<SnmpOidReceived, Unit>(oidMapService, NullLogger<OidResolutionBehavior<SnmpOidReceived, Unit>>.Instance);
        var notification = MakeNotification("1.3.9.9.9.9");

        await behavior.Handle(notification, ct => Task.FromResult(Unit.Value), CancellationToken.None);

        Assert.Equal(OidMapService.Unknown, notification.MetricName);
    }

    [Fact]
    public async Task AlwaysCallsNext_KnownOid()
    {
        var oidMapService = new StubOidMapService(knownOid: "1.3.6.1.2.1.25.3.3.1.2", metricName: "hrProcessorLoad");
        var behavior = new OidResolutionBehavior<SnmpOidReceived, Unit>(oidMapService, NullLogger<OidResolutionBehavior<SnmpOidReceived, Unit>>.Instance);
        var notification = MakeNotification("1.3.6.1.2.1.25.3.3.1.2");
        var nextCalled = false;

        await behavior.Handle(notification, ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AlwaysCallsNext_UnknownOid()
    {
        // Even when OID is absent (Unknown sentinel), next() must still be called
        var oidMapService = new StubOidMapService(knownOid: null, metricName: null);
        var behavior = new OidResolutionBehavior<SnmpOidReceived, Unit>(oidMapService, NullLogger<OidResolutionBehavior<SnmpOidReceived, Unit>>.Instance);
        var notification = MakeNotification("9.9.9.9.9");
        var nextCalled = false;

        await behavior.Handle(notification, ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task ResolvesHeartbeatOid_ViaOidMapService()
    {
        // Heartbeat OID resolves to "Heartbeat" via OidMapService — no special-case bypassing
        var oidMapService = new StubOidMapService(
            knownOid: HeartbeatJobOptions.HeartbeatOid,
            metricName: "Heartbeat");
        var behavior = new OidResolutionBehavior<SnmpOidReceived, Unit>(
            oidMapService, NullLogger<OidResolutionBehavior<SnmpOidReceived, Unit>>.Instance);
        var notification = new SnmpOidReceived
        {
            Oid = HeartbeatJobOptions.HeartbeatOid,
            AgentIp = IPAddress.Parse("127.0.0.1"),
            Value = new Counter32(1),
            Source = SnmpSource.Trap,
            TypeCode = SnmpType.Counter32,
            DeviceName = HeartbeatJobOptions.HeartbeatDeviceName,
        };
        var nextCalled = false;

        await behavior.Handle(notification, ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.Equal("Heartbeat", notification.MetricName);  // OidMapService resolved it
        Assert.True(nextCalled);                              // Pipeline still continues
    }

    // --- Stub IOidMapService ---

    private sealed class StubOidMapService : IOidMapService
    {
        private readonly string? _knownOid;
        private readonly string? _metricName;

        public StubOidMapService(string? knownOid, string? metricName)
        {
            _knownOid = knownOid;
            _metricName = metricName;
        }

        public string Resolve(string oid) =>
            oid == _knownOid && _metricName is not null
                ? _metricName
                : OidMapService.Unknown;

        public int EntryCount => _knownOid is not null ? 1 : 0;

        public bool ContainsMetricName(string metricName) =>
            metricName == _metricName;

        public void UpdateMap(Dictionary<string, string> entries) { }

        public string? ResolveToOid(string metricName) => null;
    }
}
