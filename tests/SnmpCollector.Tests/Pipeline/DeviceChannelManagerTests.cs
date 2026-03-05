using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using SnmpCollector.Telemetry;
using Lextm.SharpSnmpLib;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

/// <summary>
/// Unit tests for <see cref="DeviceChannelManager"/> verifying channel creation,
/// TryWrite/ReadAllAsync end-to-end, DropOldest backpressure, CompleteAll, and
/// KeyNotFoundException for unknown device names.
/// </summary>
public sealed class DeviceChannelManagerTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly PipelineMetricService _metrics;

    public DeviceChannelManagerTests()
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        _sp = services.BuildServiceProvider();
        _metrics = new PipelineMetricService(
            _sp.GetRequiredService<IMeterFactory>(),
            Options.Create(new SiteOptions { Name = "test-site" }));
    }

    public void Dispose()
    {
        _metrics.Dispose();
        _sp.Dispose();
    }

    private static DeviceInfo MakeDevice(string name, string ip = "10.0.0.1")
        => new DeviceInfo(name, ip, "public", []);

    private DeviceChannelManager CreateManager(IReadOnlyList<DeviceInfo> devices, int capacity = 100)
    {
        var registry = new StubDeviceRegistry(devices);
        var options = Options.Create(new ChannelsOptions { BoundedCapacity = capacity });
        return new DeviceChannelManager(registry, options, _metrics, NullLogger<DeviceChannelManager>.Instance);
    }

    // -----------------------------------------------------------------------
    // 1. CreatesChannelForEachDevice
    // -----------------------------------------------------------------------

    [Fact]
    public void CreatesChannelForEachDevice()
    {
        var devices = new[]
        {
            MakeDevice("device-a", "10.0.0.1"),
            MakeDevice("device-b", "10.0.0.2"),
            MakeDevice("device-c", "10.0.0.3"),
        };

        var manager = CreateManager(devices);

        Assert.Equal(3, manager.DeviceNames.Count);
        Assert.Contains("device-a", manager.DeviceNames);
        Assert.Contains("device-b", manager.DeviceNames);
        Assert.Contains("device-c", manager.DeviceNames);
    }

    // -----------------------------------------------------------------------
    // 2. GetWriter_ReturnsWriterForKnownDevice
    // -----------------------------------------------------------------------

    [Fact]
    public void GetWriter_ReturnsWriterForKnownDevice()
    {
        var manager = CreateManager([MakeDevice("alpha", "10.0.0.1")]);

        var writer = manager.GetWriter("alpha");

        Assert.NotNull(writer);
    }

    // -----------------------------------------------------------------------
    // 3. GetReader_ReturnsReaderForKnownDevice
    // -----------------------------------------------------------------------

    [Fact]
    public void GetReader_ReturnsReaderForKnownDevice()
    {
        var manager = CreateManager([MakeDevice("beta", "10.0.0.1")]);

        var reader = manager.GetReader("beta");

        Assert.NotNull(reader);
    }

    // -----------------------------------------------------------------------
    // 4. TryWrite_And_ReadAllAsync_EndToEnd
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TryWrite_And_ReadAllAsync_EndToEnd()
    {
        var manager = CreateManager([MakeDevice("writer-test", "10.0.0.1")], capacity: 10);

        var envelope = new VarbindEnvelope(
            Oid: "1.3.6.1.2.1.1.1.0",
            Value: new OctetString("hello"),
            TypeCode: SnmpType.OctetString,
            AgentIp: IPAddress.Parse("10.0.0.1"),
            DeviceName: "writer-test");

        var writer = manager.GetWriter("writer-test");
        var written = writer.TryWrite(envelope);
        Assert.True(written);

        // Complete the channel so ReadAllAsync finishes
        manager.CompleteAll();

        var reader = manager.GetReader("writer-test");
        var received = new List<VarbindEnvelope>();
        await foreach (var item in reader.ReadAllAsync())
            received.Add(item);

        Assert.Single(received);
        Assert.Equal("1.3.6.1.2.1.1.1.0", received[0].Oid);
        Assert.Equal("writer-test", received[0].DeviceName);
        Assert.Equal(IPAddress.Parse("10.0.0.1"), received[0].AgentIp);
    }

    // -----------------------------------------------------------------------
    // 5. DropOldest_DropsWhenCapacityExceeded
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DropOldest_DropsWhenCapacityExceeded()
    {
        // capacity=2: writing 3 items should drop the oldest (first written)
        var manager = CreateManager([MakeDevice("drop-test", "10.0.0.1")], capacity: 2);

        var writer = manager.GetWriter("drop-test");

        var e1 = MakeEnvelope("drop-test", "1.3.6.1.1");
        var e2 = MakeEnvelope("drop-test", "1.3.6.1.2");
        var e3 = MakeEnvelope("drop-test", "1.3.6.1.3");

        writer.TryWrite(e1); // fills slot 1
        writer.TryWrite(e2); // fills slot 2 (channel full)
        writer.TryWrite(e3); // drops e1 (oldest), writes e3

        manager.CompleteAll();

        var reader = manager.GetReader("drop-test");
        var received = new List<VarbindEnvelope>();
        await foreach (var item in reader.ReadAllAsync())
            received.Add(item);

        // e1 should have been dropped; e2 and e3 remain
        Assert.Equal(2, received.Count);
        Assert.Equal("1.3.6.1.2", received[0].Oid);
        Assert.Equal("1.3.6.1.3", received[1].Oid);
    }

    // -----------------------------------------------------------------------
    // 6. CompleteAll_AllowsReadAllAsyncToComplete
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CompleteAll_AllowsReadAllAsyncToComplete()
    {
        var manager = CreateManager([MakeDevice("complete-test", "10.0.0.1")], capacity: 10);

        var writer = manager.GetWriter("complete-test");
        writer.TryWrite(MakeEnvelope("complete-test", "1.3.6.1.1.1"));
        manager.CompleteAll();

        var reader = manager.GetReader("complete-test");
        var count = 0;
        await foreach (var _ in reader.ReadAllAsync())
            count++;

        // ReadAllAsync completed (not hung) and yielded the 1 item
        Assert.Equal(1, count);
    }

    // -----------------------------------------------------------------------
    // 7. GetWriter_ThrowsForUnknownDevice
    // -----------------------------------------------------------------------

    [Fact]
    public void GetWriter_ThrowsForUnknownDevice()
    {
        var manager = CreateManager([MakeDevice("known", "10.0.0.1")]);

        Assert.Throws<KeyNotFoundException>(() => manager.GetWriter("nonexistent"));
    }

    // -----------------------------------------------------------------------
    // 8. GetReader_ThrowsForUnknownDevice
    // -----------------------------------------------------------------------

    [Fact]
    public void GetReader_ThrowsForUnknownDevice()
    {
        var manager = CreateManager([MakeDevice("known", "10.0.0.1")]);

        Assert.Throws<KeyNotFoundException>(() => manager.GetReader("nonexistent"));
    }

    // -----------------------------------------------------------------------
    // 9. WaitForDrainAsync_CompletesAfterCompleteAll
    // -----------------------------------------------------------------------

    [Fact]
    public async Task WaitForDrainAsync_CompletesAfterCompleteAll()
    {
        var manager = CreateManager([MakeDevice("drain-test", "10.0.0.1")], capacity: 10);

        // Write an item
        var writer = manager.GetWriter("drain-test");
        writer.TryWrite(MakeEnvelope("drain-test", "1.3.6.1.1"));

        // Start a background consumer to drain the channel
        var reader = manager.GetReader("drain-test");
        var consumerTask = Task.Run(async () =>
        {
            await foreach (var _ in reader.ReadAllAsync()) { }
        });

        // Complete all channels (marks writers done)
        manager.CompleteAll();

        // WaitForDrainAsync should complete once consumer finishes
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await manager.WaitForDrainAsync(cts.Token);
        await consumerTask;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static VarbindEnvelope MakeEnvelope(string deviceName, string oid)
        => new VarbindEnvelope(
            Oid: oid,
            Value: new Integer32(1),
            TypeCode: SnmpType.Integer32,
            AgentIp: IPAddress.Parse("10.0.0.1"),
            DeviceName: deviceName);

    /// <summary>Stub IDeviceRegistry backed by a fixed list of devices.</summary>
    private sealed class StubDeviceRegistry : IDeviceRegistry
    {
        private readonly IReadOnlyList<DeviceInfo> _devices;

        public StubDeviceRegistry(IReadOnlyList<DeviceInfo> devices)
            => _devices = devices;

        public IReadOnlyList<DeviceInfo> AllDevices => _devices;

        public bool TryGetDevice(IPAddress senderIp, [NotNullWhen(true)] out DeviceInfo? device)
        {
            device = _devices.FirstOrDefault(d =>
                IPAddress.Parse(d.IpAddress).Equals(senderIp));
            return device is not null;
        }

        public bool TryGetDeviceByName(string deviceName, [NotNullWhen(true)] out DeviceInfo? device)
        {
            device = _devices.FirstOrDefault(d =>
                string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));
            return device is not null;
        }
    }
}
