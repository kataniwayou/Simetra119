using Microsoft.Extensions.Logging.Abstractions;
using SnmpCollector.Pipeline;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

public sealed class DeviceRegistryTests
{
    /// <summary>
    /// Creates two pre-built DeviceInfo objects for use in registry tests.
    /// - npb-core-01 at 10.0.10.1:161 with one poll group
    /// - obp-edge-01 at 10.0.10.2:161 with no poll groups
    /// </summary>
    private static List<DeviceInfo> TwoDeviceInfos() => new()
    {
        new DeviceInfo("npb-core-01", "10.0.10.1", "10.0.10.1", 161,
            new List<MetricPollInfo>
            {
                new(0, new List<string> { "1.3.6.1.2.1.25.3.3.1.2" }.AsReadOnly(), 30, 0.8)
            }.AsReadOnly(),
            "Simetra.npb-core-01"),
        new DeviceInfo("obp-edge-01", "10.0.10.2", "10.0.10.2", 161,
            Array.Empty<MetricPollInfo>().ToList().AsReadOnly(),
            "Simetra.obp-edge-01")
    };

    private static DeviceRegistry CreateRegistry()
    {
        return new DeviceRegistry(NullLogger<DeviceRegistry>.Instance);
    }

    // -------------------------------------------------------------------------
    // Constructor starts empty (new behavior post-refactor)
    // -------------------------------------------------------------------------

    [Fact]
    public void Constructor_StartsEmpty()
    {
        var sut = CreateRegistry();

        Assert.Empty(sut.AllDevices);
    }

    [Fact]
    public void Constructor_TryGetByIpPort_ReturnsFalse()
    {
        var sut = CreateRegistry();

        var found = sut.TryGetByIpPort("10.0.10.1", 161, out _);

        Assert.False(found);
    }

    // -------------------------------------------------------------------------
    // TryGetDeviceByName (secondary lookup, preserved for trap listener)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetDeviceByName_ExactMatch_ReturnsDevice()
    {
        var sut = CreateRegistry();
        await sut.ReloadAsync(TwoDeviceInfos());

        var found = sut.TryGetDeviceByName("npb-core-01", out var device);

        Assert.True(found);
        Assert.NotNull(device);
        Assert.Equal("npb-core-01", device.Name);
    }

    [Fact]
    public async Task TryGetDeviceByName_CaseInsensitive_ReturnsDevice()
    {
        var sut = CreateRegistry();
        await sut.ReloadAsync(TwoDeviceInfos());

        var found = sut.TryGetDeviceByName("NPB-CORE-01", out var device);

        Assert.True(found);
        Assert.NotNull(device);
        Assert.Equal("npb-core-01", device.Name);
    }

    [Fact]
    public async Task TryGetDeviceByName_Unknown_ReturnsFalse()
    {
        var sut = CreateRegistry();
        await sut.ReloadAsync(TwoDeviceInfos());

        var found = sut.TryGetDeviceByName("nonexistent", out var device);

        Assert.False(found);
        Assert.Null(device);
    }

    // -------------------------------------------------------------------------
    // TryGetByIpPort (primary lookup)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetByIpPort_ExactMatch_ReturnsDevice()
    {
        var sut = CreateRegistry();
        await sut.ReloadAsync(TwoDeviceInfos());

        var found = sut.TryGetByIpPort("10.0.10.1", 161, out var device);

        Assert.True(found);
        Assert.NotNull(device);
        Assert.Equal("npb-core-01", device.Name);
        Assert.Equal("10.0.10.1", device.ConfigAddress);
        Assert.Equal(161, device.Port);
    }

    [Fact]
    public async Task TryGetByIpPort_Unknown_ReturnsFalse()
    {
        var sut = CreateRegistry();
        await sut.ReloadAsync(TwoDeviceInfos());

        var found = sut.TryGetByIpPort("99.99.99.99", 9999, out var device);

        Assert.False(found);
        Assert.Null(device);
    }

    // -------------------------------------------------------------------------
    // AllDevices
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AllDevices_ReturnsAllRegistered()
    {
        var sut = CreateRegistry();
        await sut.ReloadAsync(TwoDeviceInfos());

        Assert.Equal(2, sut.AllDevices.Count);
    }

    [Fact]
    public async Task AllDevices_AfterEmptyReload_ReturnsEmpty()
    {
        var sut = CreateRegistry();
        await sut.ReloadAsync(TwoDeviceInfos());

        await sut.ReloadAsync(new List<DeviceInfo>());

        Assert.Empty(sut.AllDevices);
    }

    // -------------------------------------------------------------------------
    // JobKey format
    // -------------------------------------------------------------------------

    [Fact]
    public void JobKey_ProducesCorrectIdentity()
    {
        var pollInfo = new MetricPollInfo(
            PollIndex: 0,
            Oids: new List<string> { "1.3.6.1.2.1.25.3.3.1.2" }.AsReadOnly(),
            IntervalSeconds: 30);

        var key = pollInfo.JobKey("10.0.10.1", 161);

        Assert.Equal("metric-poll-10.0.10.1_161-0", key);
    }

    // -------------------------------------------------------------------------
    // ReloadAsync: atomic swap, added/removed sets
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReloadAsync_AddsNewDevice_FoundByName()
    {
        var sut = CreateRegistry();
        await sut.ReloadAsync(TwoDeviceInfos());

        // Reload with original two plus a new device
        var newInfos = new List<DeviceInfo>(TwoDeviceInfos())
        {
            new("new-device", "10.0.10.3", "10.0.10.3", 161,
                Array.Empty<MetricPollInfo>().ToList().AsReadOnly(),
                "Simetra.new-device")
        };

        var (added, removed) = await sut.ReloadAsync(newInfos);

        Assert.Contains("10.0.10.3:161", added);
        Assert.Empty(removed);
        Assert.Equal(3, sut.AllDevices.Count);

        var found = sut.TryGetDeviceByName("new-device", out var device);
        Assert.True(found);
        Assert.Equal("new-device", device!.Name);

        var foundByIp = sut.TryGetByIpPort("10.0.10.3", 161, out var deviceByIp);
        Assert.True(foundByIp);
        Assert.Equal("new-device", deviceByIp!.Name);
    }

    [Fact]
    public async Task ReloadAsync_RemovesDevice_NotFoundByName()
    {
        var sut = CreateRegistry();
        await sut.ReloadAsync(TwoDeviceInfos());

        // Reload with only one of the original two devices
        var newInfos = new List<DeviceInfo>
        {
            new("npb-core-01", "10.0.10.1", "10.0.10.1", 161,
                Array.Empty<MetricPollInfo>().ToList().AsReadOnly(),
                "Simetra.npb-core-01")
        };

        var (added, removed) = await sut.ReloadAsync(newInfos);

        Assert.Empty(added);
        Assert.Contains("10.0.10.2:161", removed);
        Assert.Single(sut.AllDevices);

        var found = sut.TryGetDeviceByName("obp-edge-01", out _);
        Assert.False(found);

        var foundByIp = sut.TryGetByIpPort("10.0.10.2", 161, out _);
        Assert.False(foundByIp);
    }

    [Fact]
    public async Task ReloadAsync_ReplacesExistingData()
    {
        var sut = CreateRegistry();
        await sut.ReloadAsync(TwoDeviceInfos());

        // Reload with a completely different set
        var newInfos = new List<DeviceInfo>
        {
            new("new-device-a", "192.168.1.1", "192.168.1.1", 162,
                Array.Empty<MetricPollInfo>().ToList().AsReadOnly(),
                "Simetra.new-device-a")
        };

        await sut.ReloadAsync(newInfos);

        // Old devices gone
        Assert.False(sut.TryGetDeviceByName("npb-core-01", out _));
        Assert.False(sut.TryGetDeviceByName("obp-edge-01", out _));

        // New device present
        Assert.True(sut.TryGetDeviceByName("new-device-a", out _));
        Assert.Single(sut.AllDevices);
    }

    [Fact]
    public async Task ReloadAsync_WithEmptyList_ClearsRegistry()
    {
        var sut = CreateRegistry();
        await sut.ReloadAsync(TwoDeviceInfos());
        Assert.Equal(2, sut.AllDevices.Count);

        var (added, removed) = await sut.ReloadAsync(new List<DeviceInfo>());

        Assert.Empty(added);
        Assert.Equal(2, removed.Count);
        Assert.Empty(sut.AllDevices);
    }

    [Fact]
    public async Task ReloadAsync_CommunityString_StoredOnDeviceInfo()
    {
        var sut = CreateRegistry();
        await sut.ReloadAsync(TwoDeviceInfos());

        var found = sut.TryGetDeviceByName("npb-core-01", out var device);
        Assert.True(found);
        Assert.Equal("Simetra.npb-core-01", device!.CommunityString);
    }

    [Fact]
    public async Task ReloadAsync_PollGroups_StoredOnDeviceInfo()
    {
        var sut = CreateRegistry();
        await sut.ReloadAsync(TwoDeviceInfos());

        var found = sut.TryGetByIpPort("10.0.10.1", 161, out var device);
        Assert.True(found);
        Assert.NotNull(device);
        Assert.Single(device.PollGroups);
        Assert.Contains("1.3.6.1.2.1.25.3.3.1.2", device.PollGroups[0].Oids);
    }
}
