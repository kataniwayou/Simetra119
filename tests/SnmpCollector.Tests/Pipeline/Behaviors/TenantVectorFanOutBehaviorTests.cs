using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Net;
using Lextm.SharpSnmpLib;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using SnmpCollector.Pipeline.Behaviors;
using SnmpCollector.Telemetry;
using SnmpCollector.Tests.Helpers;
using Xunit;

namespace SnmpCollector.Tests.Pipeline.Behaviors;

/// <summary>
/// Unit tests for <see cref="TenantVectorFanOutBehavior{TNotification,TResponse}"/>.
/// Tests that verify counter increments use MeterListener and must run in the
/// NonParallelMeterTests collection to avoid cross-test meter contamination.
/// </summary>
[Collection(NonParallelCollection.Name)]
public sealed class TenantVectorFanOutBehaviorTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly PipelineMetricService _pipelineMetrics;
    private readonly MeterListener _listener;
    private readonly List<(string InstrumentName, long Value, KeyValuePair<string, object?>[] Tags)> _measurements = new();

    public TenantVectorFanOutBehaviorTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        _sp = services.BuildServiceProvider();

        _pipelineMetrics = new PipelineMetricService(_sp.GetRequiredService<IMeterFactory>());

        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == TelemetryConstants.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            _measurements.Add((instrument.Name, value, tags.ToArray()));
        });
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _pipelineMetrics.Dispose();
        _sp.Dispose();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static SnmpOidReceived MakeNotification(
        string metricName,
        string deviceName = "test-device",
        string agentIp = "10.0.0.1",
        double extractedValue = 42.0,
        string? extractedStringValue = null,
        SnmpType typeCode = SnmpType.Gauge32)
        => new()
        {
            Oid = "1.3.6.1.2.1.25.3.3.1.2",
            AgentIp = IPAddress.Parse(agentIp),
            Value = new Gauge32((uint)extractedValue),
            Source = SnmpSource.Poll,
            TypeCode = typeCode,
            DeviceName = deviceName,
            MetricName = metricName,
            ExtractedValue = extractedValue,
            ExtractedStringValue = extractedStringValue
        };

    private TenantVectorFanOutBehavior<SnmpOidReceived, Unit> CreateBehavior(
        ITenantVectorRegistry registry,
        IDeviceRegistry deviceRegistry)
        => new(
            registry,
            deviceRegistry,
            _pipelineMetrics,
            NullLogger<TenantVectorFanOutBehavior<SnmpOidReceived, Unit>>.Instance);

    // -----------------------------------------------------------------------
    // 1. Routes matching sample to slot
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RoutesMatchingSampleToSlot()
    {
        // Arrange: real registry with a route for (10.0.0.1, 161, hrProcessorLoad)
        var registry = CreateRegistryWithRoute("10.0.0.1", 161, "hrProcessorLoad");
        var deviceRegistry = new StubDeviceRegistry("test-device", "10.0.0.1", 161);
        var behavior = CreateBehavior(registry, deviceRegistry);

        var msg = MakeNotification("hrProcessorLoad", extractedValue: 75.0, typeCode: SnmpType.Gauge32);
        var nextCalled = false;

        // Act
        await behavior.Handle(msg, ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        // Assert: slot was written
        Assert.True(registry.TryRoute("10.0.0.1", 161, "hrProcessorLoad", out var holders));
        var slot = holders[0].ReadSlot();
        Assert.NotNull(slot);
        Assert.Equal(75.0, slot.Value);
        Assert.Equal(SnmpType.Gauge32, holders[0].TypeCode);
        Assert.True(nextCalled);
    }

    // -----------------------------------------------------------------------
    // 2. Skips sample with MetricName == "Unknown"
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SkipsUnknownMetricName()
    {
        var registry = new TrackingRegistry();
        var deviceRegistry = new StubDeviceRegistry("test-device", "10.0.0.1", 161);
        var behavior = CreateBehavior(registry, deviceRegistry);

        var msg = MakeNotification(OidMapService.Unknown);
        var nextCalled = false;

        await behavior.Handle(msg, ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.Equal(0, registry.TryRouteCallCount);
        Assert.True(nextCalled);
    }

    // -----------------------------------------------------------------------
    // 3. Skips sample with null MetricName
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SkipsNullMetricName()
    {
        var registry = new TrackingRegistry();
        var deviceRegistry = new StubDeviceRegistry("test-device", "10.0.0.1", 161);
        var behavior = CreateBehavior(registry, deviceRegistry);

        var msg = MakeNotification(null!);
        msg.MetricName = null; // force null (MakeNotification sets MetricName)
        var nextCalled = false;

        await behavior.Handle(msg, ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.Equal(0, registry.TryRouteCallCount);
        Assert.True(nextCalled);
    }

    // -----------------------------------------------------------------------
    // 4. Skips when device not found in device registry
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SkipsWhenDeviceNotInRegistry()
    {
        var registry = new TrackingRegistry();
        var deviceRegistry = new StubDeviceRegistry(null, null, 0); // no device found
        var behavior = CreateBehavior(registry, deviceRegistry);

        var msg = MakeNotification("hrProcessorLoad");
        var nextCalled = false;

        await behavior.Handle(msg, ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.Equal(0, registry.TryRouteCallCount);
        Assert.True(nextCalled);
    }

    // -----------------------------------------------------------------------
    // 5. Skips when device resolves but no matching route
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SkipsWhenNoMatchingRoute()
    {
        // Real registry with no tenants loaded — TryRoute returns false
        var registry = new TenantVectorRegistry(
            NSubstitute.Substitute.For<IDeviceRegistry>(),
            NSubstitute.Substitute.For<IOidMapService>(),
            NullLogger<TenantVectorRegistry>.Instance);
        var deviceRegistry = new StubDeviceRegistry("test-device", "10.0.0.1", 161);
        var behavior = CreateBehavior(registry, deviceRegistry);

        var msg = MakeNotification("hrProcessorLoad");
        var nextCalled = false;

        await behavior.Handle(msg, ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        // No slot writes — no holders. next() still called.
        Assert.True(nextCalled);
    }

    // -----------------------------------------------------------------------
    // 6. Always calls next even when registry throws
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AlwaysCallsNextEvenOnException()
    {
        var registry = new ThrowingRegistry();
        var deviceRegistry = new StubDeviceRegistry("test-device", "10.0.0.1", 161);
        var behavior = CreateBehavior(registry, deviceRegistry);

        var msg = MakeNotification("hrProcessorLoad");
        var nextCalled = false;

        // Should NOT throw — exception is caught internally
        await behavior.Handle(msg, ct =>
        {
            nextCalled = true;
            return Task.FromResult(Unit.Value);
        }, CancellationToken.None);

        Assert.True(nextCalled);
    }

    // -----------------------------------------------------------------------
    // 7. Increments counter once per slot write
    // -----------------------------------------------------------------------

    [Fact]
    public async Task IncrementsCounterPerSlotWrite()
    {
        // Two tenants share the same route — fan-out to 2 holders
        var registry = CreateRegistryWithTwoTenants("10.0.0.1", 161, "ifInOctets");
        var deviceRegistry = new StubDeviceRegistry("test-device", "10.0.0.1", 161);
        var behavior = CreateBehavior(registry, deviceRegistry);

        var msg = MakeNotification("ifInOctets", extractedValue: 99.0);

        await behavior.Handle(msg, ct => Task.FromResult(Unit.Value), CancellationToken.None);

        // 2 holders → 2 counter increments
        var matches = _measurements.Where(m => m.InstrumentName == "snmp.tenantvector.routed").ToList();
        Assert.Equal(2, matches.Count);
        Assert.All(matches, m =>
        {
            Assert.Equal(1L, m.Value);
            var tags = m.Tags.ToDictionary(t => t.Key, t => t.Value);
            Assert.Equal("test-device", tags["device_name"]);
        });
    }

    // -----------------------------------------------------------------------
    // 8. Fan-out writes to all matching tenant holders
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FanOutToMultipleTenants()
    {
        // Three tenants all monitor the same metric on the same device
        var registry = CreateRegistryWithThreeTenants("10.0.0.1", 161, "sysUpTime");
        var deviceRegistry = new StubDeviceRegistry("test-device", "10.0.0.1", 161);
        var behavior = CreateBehavior(registry, deviceRegistry);

        var msg = MakeNotification("sysUpTime", extractedValue: 123456.0, typeCode: SnmpType.TimeTicks);

        await behavior.Handle(msg, ct => Task.FromResult(Unit.Value), CancellationToken.None);

        Assert.True(registry.TryRoute("10.0.0.1", 161, "sysUpTime", out var holders));
        Assert.Equal(3, holders.Count);
        Assert.All(holders, h =>
        {
            var slot = h.ReadSlot();
            Assert.NotNull(slot);
            Assert.Equal(123456.0, slot.Value);
            Assert.Equal(SnmpType.TimeTicks, h.TypeCode);
        });
    }

    // -----------------------------------------------------------------------
    // Registry / device registry factory helpers
    // -----------------------------------------------------------------------

    private static TenantVectorRegistry CreateRegistryWithRoute(string ip, int port, string metricName)
    {
        var registry = new TenantVectorRegistry(
            NSubstitute.Substitute.For<IDeviceRegistry>(),
            NSubstitute.Substitute.For<IOidMapService>(),
            NullLogger<TenantVectorRegistry>.Instance);
        registry.Reload(new TenantVectorOptions
        {
            Tenants = new List<TenantOptions>
            {
                new()
                {
                    Priority = 1,
                    Metrics = new List<MetricSlotOptions>
                    {
                        new() { Ip = ip, Port = port, MetricName = metricName }
                    }
                }
            }
        });
        return registry;
    }

    private static TenantVectorRegistry CreateRegistryWithTwoTenants(string ip, int port, string metricName)
    {
        var registry = new TenantVectorRegistry(
            NSubstitute.Substitute.For<IDeviceRegistry>(),
            NSubstitute.Substitute.For<IOidMapService>(),
            NullLogger<TenantVectorRegistry>.Instance);
        registry.Reload(new TenantVectorOptions
        {
            Tenants = new List<TenantOptions>
            {
                new()
                {
                    Priority = 1,
                    Metrics = new List<MetricSlotOptions>
                    {
                        new() { Ip = ip, Port = port, MetricName = metricName }
                    }
                },
                new()
                {
                    Priority = 2,
                    Metrics = new List<MetricSlotOptions>
                    {
                        new() { Ip = ip, Port = port, MetricName = metricName }
                    }
                }
            }
        });
        return registry;
    }

    private static TenantVectorRegistry CreateRegistryWithThreeTenants(string ip, int port, string metricName)
    {
        var registry = new TenantVectorRegistry(
            NSubstitute.Substitute.For<IDeviceRegistry>(),
            NSubstitute.Substitute.For<IOidMapService>(),
            NullLogger<TenantVectorRegistry>.Instance);
        registry.Reload(new TenantVectorOptions
        {
            Tenants = new List<TenantOptions>
            {
                new()
                {
                    Priority = 1,
                    Metrics = new List<MetricSlotOptions>
                    {
                        new() { Ip = ip, Port = port, MetricName = metricName }
                    }
                },
                new()
                {
                    Priority = 2,
                    Metrics = new List<MetricSlotOptions>
                    {
                        new() { Ip = ip, Port = port, MetricName = metricName }
                    }
                },
                new()
                {
                    Priority = 3,
                    Metrics = new List<MetricSlotOptions>
                    {
                        new() { Ip = ip, Port = port, MetricName = metricName }
                    }
                }
            }
        });
        return registry;
    }

    // -----------------------------------------------------------------------
    // Stub / tracking implementations
    // -----------------------------------------------------------------------

    /// <summary>Stub device registry that returns a single configurable device or no device.</summary>
    private sealed class StubDeviceRegistry : IDeviceRegistry
    {
        private readonly DeviceInfo? _device;

        public StubDeviceRegistry(string? name, string? ip, int port)
        {
            if (name is not null && ip is not null)
                _device = new DeviceInfo(name, ip, ip, port, Array.Empty<MetricPollInfo>(), $"Simetra.{name}");
        }

        public bool TryGetByIpPort(string configAddress, int port, [NotNullWhen(true)] out DeviceInfo? device)
        {
            device = _device;
            return _device is not null;
        }

        public bool TryGetDeviceByName(string deviceName, [NotNullWhen(true)] out DeviceInfo? device)
        {
            device = _device;
            return _device is not null;
        }

        public IReadOnlyList<DeviceInfo> AllDevices =>
            _device is not null ? new[] { _device } : Array.Empty<DeviceInfo>();

        public Task<(IReadOnlySet<string> Added, IReadOnlySet<string> Removed)> ReloadAsync(
            List<DeviceOptions> devices)
            => throw new NotSupportedException("Not needed in tests");
    }

    /// <summary>Tracking registry that records TryRoute call count but never routes.</summary>
    private sealed class TrackingRegistry : ITenantVectorRegistry
    {
        public int TryRouteCallCount { get; private set; }

        public IReadOnlyList<PriorityGroup> Groups => Array.Empty<PriorityGroup>();
        public int TenantCount => 0;
        public int SlotCount => 0;

        public bool TryRoute(string ip, int port, string metricName,
            out IReadOnlyList<MetricSlotHolder> holders)
        {
            TryRouteCallCount++;
            holders = Array.Empty<MetricSlotHolder>();
            return false;
        }
    }

    /// <summary>Registry that always throws to verify exception isolation.</summary>
    private sealed class ThrowingRegistry : ITenantVectorRegistry
    {
        public IReadOnlyList<PriorityGroup> Groups => Array.Empty<PriorityGroup>();
        public int TenantCount => 0;
        public int SlotCount => 0;

        public bool TryRoute(string ip, int port, string metricName,
            out IReadOnlyList<MetricSlotHolder> holders)
        {
            holders = Array.Empty<MetricSlotHolder>();
            throw new InvalidOperationException("Simulated registry failure");
        }
    }
}
