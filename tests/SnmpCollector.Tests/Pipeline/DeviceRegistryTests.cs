using System.Net;
using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

public sealed class DeviceRegistryTests
{
    /// <summary>
    /// Creates a DevicesOptions with two devices:
    ///   - npb-core-01 at 10.0.10.1
    ///   - obp-edge-01 at 10.0.10.2
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
            Options.Create(devicesOptions ?? TwoDeviceOptions()));
    }

    [Fact]
    public void TryGetDevice_KnownIp_ReturnsDevice()
    {
        var sut = CreateRegistry();

        var found = sut.TryGetDevice(IPAddress.Parse("10.0.10.1"), out var device);

        Assert.True(found);
        Assert.NotNull(device);
        Assert.Equal("npb-core-01", device.Name);
    }

    [Fact]
    public void TryGetDevice_Ipv6Mapped_ReturnsDevice()
    {
        var sut = CreateRegistry();

        // IPv6-mapped IPv4 address should normalize to the IPv4 address
        var found = sut.TryGetDevice(IPAddress.Parse("::ffff:10.0.10.1"), out var device);

        Assert.True(found);
        Assert.NotNull(device);
        Assert.Equal("npb-core-01", device.Name);
    }

    [Fact]
    public void TryGetDevice_UnknownIp_ReturnsFalse()
    {
        var sut = CreateRegistry();

        var found = sut.TryGetDevice(IPAddress.Parse("192.168.99.99"), out var device);

        Assert.False(found);
        Assert.Null(device);
    }

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

    [Fact]
    public void AllDevices_ReturnsAllRegistered()
    {
        var sut = CreateRegistry();

        Assert.Equal(2, sut.AllDevices.Count);
    }

    [Fact]
    public void JobKey_ProducesCorrectIdentity()
    {
        var pollInfo = new MetricPollInfo(
            PollIndex: 0,
            Oids: new List<string> { "1.3.6.1.2.1.25.3.3.1.2" }.AsReadOnly(),
            IntervalSeconds: 30);

        var key = pollInfo.JobKey("npb-core-01");

        Assert.Equal("metric-poll-npb-core-01-0", key);
    }
}
