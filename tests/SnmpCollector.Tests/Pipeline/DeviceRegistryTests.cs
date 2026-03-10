using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

public sealed class DeviceRegistryTests
{
    /// <summary>
    /// Creates a DevicesOptions with two devices:
    ///   - npb-core-01 at 10.0.10.1:161
    ///   - obp-edge-01 at 10.0.10.2:161
    /// </summary>
    private static DevicesOptions TwoDeviceOptions() => new()
    {
        Devices =
        [
            new DeviceOptions
            {
                Name = "npb-core-01",
                IpAddress = "10.0.10.1",
                MetricPolls =
                [
                    new MetricPollOptions
                    {
                        Oids = ["1.3.6.1.2.1.25.3.3.1.2"],
                        IntervalSeconds = 30
                    }
                ]
            },
            new DeviceOptions
            {
                Name = "obp-edge-01",
                IpAddress = "10.0.10.2",
                MetricPolls = []
            }
        ]
    };

    private static DeviceRegistry CreateRegistry(DevicesOptions? devicesOptions = null)
    {
        return new DeviceRegistry(
            Options.Create(devicesOptions ?? TwoDeviceOptions()),
            NullLogger<DeviceRegistry>.Instance);
    }

    // -------------------------------------------------------------------------
    // TryGetDeviceByName (secondary lookup, preserved for trap listener)
    // -------------------------------------------------------------------------

    [Fact]
    public void TryGetDeviceByName_ExactMatch_ReturnsDevice()
    {
        var sut = CreateRegistry();

        var found = sut.TryGetDeviceByName("npb-core-01", out var device);

        Assert.True(found);
        Assert.NotNull(device);
        Assert.Equal("npb-core-01", device.Name);
    }

    [Fact]
    public void TryGetDeviceByName_CaseInsensitive_ReturnsDevice()
    {
        var sut = CreateRegistry();

        var found = sut.TryGetDeviceByName("NPB-CORE-01", out var device);

        Assert.True(found);
        Assert.NotNull(device);
        Assert.Equal("npb-core-01", device.Name);
    }

    [Fact]
    public void TryGetDeviceByName_Unknown_ReturnsFalse()
    {
        var sut = CreateRegistry();

        var found = sut.TryGetDeviceByName("nonexistent", out var device);

        Assert.False(found);
        Assert.Null(device);
    }

    // -------------------------------------------------------------------------
    // TryGetByIpPort (primary lookup)
    // -------------------------------------------------------------------------

    [Fact]
    public void TryGetByIpPort_ExactMatch_ReturnsDevice()
    {
        var sut = CreateRegistry();

        var found = sut.TryGetByIpPort("10.0.10.1", 161, out var device);

        Assert.True(found);
        Assert.NotNull(device);
        Assert.Equal("npb-core-01", device.Name);
        Assert.Equal("10.0.10.1", device.ConfigAddress);
        Assert.Equal(161, device.Port);
    }

    [Fact]
    public void TryGetByIpPort_Unknown_ReturnsFalse()
    {
        var sut = CreateRegistry();

        var found = sut.TryGetByIpPort("99.99.99.99", 9999, out var device);

        Assert.False(found);
        Assert.Null(device);
    }

    // -------------------------------------------------------------------------
    // Duplicate IP+Port detection
    // -------------------------------------------------------------------------

    [Fact]
    public void Constructor_DuplicateIpPort_ThrowsInvalidOperationException()
    {
        var opts = new DevicesOptions
        {
            Devices =
            [
                new DeviceOptions
                {
                    Name = "device-a",
                    IpAddress = "10.0.10.1",
                    Port = 161,
                    MetricPolls = []
                },
                new DeviceOptions
                {
                    Name = "device-b",
                    IpAddress = "10.0.10.1",
                    Port = 161,
                    MetricPolls = []
                }
            ]
        };

        var ex = Assert.Throws<InvalidOperationException>(() => CreateRegistry(opts));
        Assert.Contains("Duplicate address+port 10.0.10.1:161", ex.Message);
        Assert.Contains("device-a", ex.Message);
        Assert.Contains("device-b", ex.Message);
    }

    [Fact]
    public void Constructor_DuplicateName_DifferentIpPort_Accepted()
    {
        var opts = new DevicesOptions
        {
            Devices =
            [
                new DeviceOptions
                {
                    Name = "same-name",
                    IpAddress = "10.0.10.1",
                    Port = 161,
                    MetricPolls = []
                },
                new DeviceOptions
                {
                    Name = "same-name",
                    IpAddress = "10.0.10.2",
                    Port = 161,
                    MetricPolls = []
                }
            ]
        };

        // Should not throw -- same name but different IP+Port is allowed
        var sut = CreateRegistry(opts);
        Assert.Equal(2, sut.AllDevices.Count);
    }

    // -------------------------------------------------------------------------
    // AllDevices
    // -------------------------------------------------------------------------

    [Fact]
    public void AllDevices_ReturnsAllRegistered()
    {
        var sut = CreateRegistry();

        Assert.Equal(2, sut.AllDevices.Count);
    }

    // -------------------------------------------------------------------------
    // DNS resolution and CommunityString passthrough
    // -------------------------------------------------------------------------

    [Fact]
    public void Constructor_DnsHostname_ResolvesToIpAddress()
    {
        // "localhost" is universally resolvable to 127.0.0.1
        var opts = new DevicesOptions
        {
            Devices =
            [
                new DeviceOptions
                {
                    Name = "dns-device",
                    IpAddress = "localhost",
                    MetricPolls = []
                }
            ]
        };

        var sut = CreateRegistry(opts);

        var found = sut.TryGetDeviceByName("dns-device", out var device);
        Assert.True(found);
        Assert.NotNull(device);
        // ConfigAddress should be the raw DNS name from config
        Assert.Equal("localhost", device.ConfigAddress);
        // ResolvedIp should be the resolved IP
        Assert.True(IPAddress.TryParse(device.ResolvedIp, out _), "ResolvedIp should be a resolved IP");
        Assert.Equal("127.0.0.1", device.ResolvedIp);
    }

    [Fact]
    public void Constructor_CommunityString_PassedThroughToDeviceInfo()
    {
        var opts = new DevicesOptions
        {
            Devices =
            [
                new DeviceOptions
                {
                    Name = "community-device",
                    IpAddress = "10.0.10.5",
                    CommunityString = "my-custom-community",
                    MetricPolls = []
                }
            ]
        };

        var sut = CreateRegistry(opts);

        var found = sut.TryGetDeviceByName("community-device", out var device);
        Assert.True(found);
        Assert.Equal("my-custom-community", device!.CommunityString);
    }

    [Fact]
    public void Constructor_NoCommunityString_DeviceInfoHasNull()
    {
        var sut = CreateRegistry();

        var found = sut.TryGetDeviceByName("npb-core-01", out var device);
        Assert.True(found);
        Assert.Null(device!.CommunityString);
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
    // ReloadAsync (added/removed sets now use IP:Port keys)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReloadAsync_AddsNewDevice_FoundByName()
    {
        var sut = CreateRegistry();

        // Reload with the original two plus a new device
        var newDevices = new List<DeviceOptions>
        {
            new() { Name = "npb-core-01", IpAddress = "10.0.10.1", MetricPolls = [] },
            new() { Name = "obp-edge-01", IpAddress = "10.0.10.2", MetricPolls = [] },
            new() { Name = "new-device", IpAddress = "10.0.10.3", MetricPolls = [] }
        };

        var (added, removed) = await sut.ReloadAsync(newDevices);

        Assert.Contains("10.0.10.3:161", added);
        Assert.Empty(removed);
        Assert.Equal(3, sut.AllDevices.Count);

        var found = sut.TryGetDeviceByName("new-device", out var device);
        Assert.True(found);
        Assert.Equal("new-device", device!.Name);

        // Also verify primary lookup works
        var foundByIp = sut.TryGetByIpPort("10.0.10.3", 161, out var deviceByIp);
        Assert.True(foundByIp);
        Assert.Equal("new-device", deviceByIp!.Name);
    }

    [Fact]
    public async Task ReloadAsync_RemovesDevice_NotFoundByName()
    {
        var sut = CreateRegistry();

        // Reload with only one of the original two devices
        var newDevices = new List<DeviceOptions>
        {
            new() { Name = "npb-core-01", IpAddress = "10.0.10.1", MetricPolls = [] }
        };

        var (added, removed) = await sut.ReloadAsync(newDevices);

        Assert.Empty(added);
        Assert.Contains("10.0.10.2:161", removed);
        Assert.Equal(1, sut.AllDevices.Count);

        var found = sut.TryGetDeviceByName("obp-edge-01", out _);
        Assert.False(found);

        var foundByIp = sut.TryGetByIpPort("10.0.10.2", 161, out _);
        Assert.False(foundByIp);
    }
}
