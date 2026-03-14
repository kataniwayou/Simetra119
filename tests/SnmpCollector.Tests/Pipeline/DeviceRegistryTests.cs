using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

public sealed class DeviceRegistryTests
{
    /// <summary>
    /// Creates a DevicesOptions with two devices:
    ///   - Simetra.npb-core-01 at 10.0.10.1:161 (short name: npb-core-01)
    ///   - Simetra.obp-edge-01 at 10.0.10.2:161 (short name: obp-edge-01)
    /// </summary>
    private static DevicesOptions TwoDeviceOptions() => new()
    {
        Devices =
        [
            new DeviceOptions
            {
                CommunityString = "Simetra.npb-core-01",
                IpAddress = "10.0.10.1",
                Polls =
                [
                    new PollOptions
                    {
                        MetricNames = ["1.3.6.1.2.1.25.3.3.1.2"],
                        IntervalSeconds = 30
                    }
                ]
            },
            new DeviceOptions
            {
                CommunityString = "Simetra.obp-edge-01",
                IpAddress = "10.0.10.2",
                Polls = []
            }
        ]
    };

    /// <summary>
    /// Creates a passthrough IOidMapService mock that returns the metric name itself as the OID.
    /// This preserves existing test behavior: tests that use OID strings as MetricNames
    /// will see those strings "resolved" to themselves.
    /// </summary>
    private static IOidMapService CreatePassthroughOidMapService()
    {
        var svc = Substitute.For<IOidMapService>();
        svc.ResolveToOid(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());
        return svc;
    }

    private static DeviceRegistry CreateRegistry(DevicesOptions? devicesOptions = null, IOidMapService? oidMapService = null)
    {
        var svc = oidMapService ?? CreatePassthroughOidMapService();
        return new DeviceRegistry(
            Options.Create(devicesOptions ?? TwoDeviceOptions()),
            svc,
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
                    CommunityString = "Simetra.device-a",
                    IpAddress = "10.0.10.1",
                    Port = 161,
                    Polls = []
                },
                new DeviceOptions
                {
                    CommunityString = "Simetra.device-b",
                    IpAddress = "10.0.10.1",
                    Port = 161,
                    Polls = []
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
                    CommunityString = "Simetra.same-name",
                    IpAddress = "10.0.10.1",
                    Port = 161,
                    Polls = []
                },
                new DeviceOptions
                {
                    CommunityString = "Simetra.same-name",
                    IpAddress = "10.0.10.2",
                    Port = 161,
                    Polls = []
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
                    CommunityString = "Simetra.dns-device",
                    IpAddress = "localhost",
                    Polls = []
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
    public void Constructor_CommunityString_StoredOnDeviceInfo()
    {
        var sut = CreateRegistry();

        var found = sut.TryGetDeviceByName("npb-core-01", out var device);
        Assert.True(found);
        Assert.Equal("Simetra.npb-core-01", device!.CommunityString);
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
            new() { CommunityString = "Simetra.npb-core-01", IpAddress = "10.0.10.1", Polls = [] },
            new() { CommunityString = "Simetra.obp-edge-01", IpAddress = "10.0.10.2", Polls = [] },
            new() { CommunityString = "Simetra.new-device", IpAddress = "10.0.10.3", Polls = [] }
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
            new() { CommunityString = "Simetra.npb-core-01", IpAddress = "10.0.10.1", Polls = [] }
        };

        var (added, removed) = await sut.ReloadAsync(newDevices);

        Assert.Empty(added);
        Assert.Contains("10.0.10.2:161", removed);
        Assert.Single(sut.AllDevices);

        var found = sut.TryGetDeviceByName("obp-edge-01", out _);
        Assert.False(found);

        var foundByIp = sut.TryGetByIpPort("10.0.10.2", 161, out _);
        Assert.False(foundByIp);
    }

    // -------------------------------------------------------------------------
    // Name resolution via IOidMapService (Phase 31-02)
    // -------------------------------------------------------------------------

    [Fact]
    public void ResolvedMetricNames_PopulateOids()
    {
        // Arrange
        var oidMapService = Substitute.For<IOidMapService>();
        oidMapService.ResolveToOid("obp_channel_L1").Returns("1.3.6.1.4.1.47477.10.21.1.3.4.0");
        oidMapService.ResolveToOid("obp_r1_power_L1").Returns("1.3.6.1.4.1.47477.10.21.1.3.10.0");

        var opts = new DevicesOptions
        {
            Devices =
            [
                new DeviceOptions
                {
                    CommunityString = "Simetra.obp-edge-01",
                    IpAddress = "10.0.10.2",
                    Polls =
                    [
                        new PollOptions
                        {
                            MetricNames = ["obp_channel_L1", "obp_r1_power_L1"],
                            IntervalSeconds = 10
                        }
                    ]
                }
            ]
        };

        // Act
        var sut = CreateRegistry(opts, oidMapService);

        // Assert
        var found = sut.TryGetByIpPort("10.0.10.2", 161, out var device);
        Assert.True(found);
        Assert.NotNull(device);
        Assert.Single(device.PollGroups);

        var oids = device.PollGroups[0].Oids;
        Assert.Equal(2, oids.Count);
        Assert.Contains("1.3.6.1.4.1.47477.10.21.1.3.4.0", oids);
        Assert.Contains("1.3.6.1.4.1.47477.10.21.1.3.10.0", oids);
        // Metric names should NOT be present -- OIDs, not names
        Assert.DoesNotContain("obp_channel_L1", oids);
        Assert.DoesNotContain("obp_r1_power_L1", oids);
    }

    [Fact]
    public void UnresolvableMetricName_LogsWarningAndSkipped()
    {
        // Arrange
        var oidMapService = Substitute.For<IOidMapService>();
        oidMapService.ResolveToOid("valid_name").Returns("1.3.6.1.4.1.47477.10.21.1.3.4.0");
        oidMapService.ResolveToOid("bad_name").Returns((string?)null);

        var logger = Substitute.For<ILogger<DeviceRegistry>>();

        var opts = new DevicesOptions
        {
            Devices =
            [
                new DeviceOptions
                {
                    CommunityString = "Simetra.test-device",
                    IpAddress = "10.0.10.5",
                    Polls =
                    [
                        new PollOptions
                        {
                            MetricNames = ["valid_name", "bad_name"],
                            IntervalSeconds = 10
                        }
                    ]
                }
            ]
        };

        // Act
        var sut = new DeviceRegistry(Options.Create(opts), oidMapService, logger);

        // Assert OID list contains only the resolved one
        var found = sut.TryGetByIpPort("10.0.10.5", 161, out var device);
        Assert.True(found);
        Assert.NotNull(device);
        Assert.Single(device.PollGroups[0].Oids);
        Assert.Equal("1.3.6.1.4.1.47477.10.21.1.3.4.0", device.PollGroups[0].Oids[0]);

        // Assert warning was logged containing the unresolvable name and expected message fragment
        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("bad_name") && o.ToString()!.Contains("not found in OID map")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void AllNamesUnresolvable_DeviceStillRegistered()
    {
        // Arrange
        var oidMapService = Substitute.For<IOidMapService>();
        oidMapService.ResolveToOid(Arg.Any<string>()).Returns((string?)null);

        var opts = new DevicesOptions
        {
            Devices =
            [
                new DeviceOptions
                {
                    CommunityString = "Simetra.no-match-device",
                    IpAddress = "10.0.10.9",
                    Polls =
                    [
                        new PollOptions
                        {
                            MetricNames = ["no_match_1", "no_match_2"],
                            IntervalSeconds = 30
                        }
                    ]
                }
            ]
        };

        // Act
        var sut = CreateRegistry(opts, oidMapService);

        // Assert device is still registered (needed for traps)
        var found = sut.TryGetByIpPort("10.0.10.9", 161, out var device);
        Assert.True(found);
        Assert.NotNull(device);
        Assert.Single(device.PollGroups);
        Assert.Empty(device.PollGroups[0].Oids);
    }

    [Fact]
    public async Task ReloadAsync_ResolvesMetricNamesToOids()
    {
        // Arrange: start with an empty registry
        var oidMapService = Substitute.For<IOidMapService>();
        oidMapService.ResolveToOid("obp_channel_L1").Returns("1.3.6.1.4.1.47477.10.21.1.3.4.0");
        oidMapService.ResolveToOid("obp_r1_power_L1").Returns("1.3.6.1.4.1.47477.10.21.1.3.10.0");

        var initialOpts = new DevicesOptions { Devices = [] };
        var sut = CreateRegistry(initialOpts, oidMapService);

        // Act: reload with a device containing MetricNames
        var reloadDevices = new List<DeviceOptions>
        {
            new()
            {
                CommunityString = "Simetra.obp-edge-01",
                IpAddress = "10.0.10.2",
                Polls =
                [
                    new PollOptions
                    {
                        MetricNames = ["obp_channel_L1", "obp_r1_power_L1"],
                        IntervalSeconds = 10
                    }
                ]
            }
        };

        await sut.ReloadAsync(reloadDevices);

        // Assert: resolved OIDs appear in PollGroups, not metric names
        var found = sut.TryGetByIpPort("10.0.10.2", 161, out var device);
        Assert.True(found);
        Assert.NotNull(device);

        var oids = device.PollGroups[0].Oids;
        Assert.Equal(2, oids.Count);
        Assert.Contains("1.3.6.1.4.1.47477.10.21.1.3.4.0", oids);
        Assert.Contains("1.3.6.1.4.1.47477.10.21.1.3.10.0", oids);
    }

    [Fact]
    public async Task ReloadAsync_UnresolvableMetricName_LogsWarning()
    {
        // Arrange
        var oidMapService = Substitute.For<IOidMapService>();
        oidMapService.ResolveToOid("valid_name").Returns("1.3.6.1.4.1.47477.10.21.1.3.4.0");
        oidMapService.ResolveToOid("bad_name").Returns((string?)null);

        var logger = Substitute.For<ILogger<DeviceRegistry>>();

        var initialOpts = new DevicesOptions { Devices = [] };
        var sut = new DeviceRegistry(Options.Create(initialOpts), oidMapService, logger);

        // Act: reload introduces a device with an unresolvable metric name
        var reloadDevices = new List<DeviceOptions>
        {
            new()
            {
                CommunityString = "Simetra.test-device",
                IpAddress = "10.0.10.5",
                Polls =
                [
                    new PollOptions
                    {
                        MetricNames = ["valid_name", "bad_name"],
                        IntervalSeconds = 10
                    }
                ]
            }
        };

        await sut.ReloadAsync(reloadDevices);

        // Assert: only valid OID is in poll group
        var found = sut.TryGetByIpPort("10.0.10.5", 161, out var device);
        Assert.True(found);
        Assert.Single(device!.PollGroups[0].Oids);
        Assert.Equal("1.3.6.1.4.1.47477.10.21.1.3.4.0", device.PollGroups[0].Oids[0]);

        // Assert: warning was logged for the unresolvable name
        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("bad_name") && o.ToString()!.Contains("not found in OID map")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }
}
