using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using SnmpCollector.Services;
using Xunit;

namespace SnmpCollector.Tests.Services;

public sealed class DeviceWatcherValidationTests
{
    private static IOidMapService CreatePassthroughOidMapService()
    {
        var svc = Substitute.For<IOidMapService>();
        svc.ResolveToOid(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());
        return svc;
    }

    private static async Task<List<DeviceInfo>> BuildAsync(
        List<DeviceOptions> devices,
        IOidMapService? oidMapService = null)
    {
        return await DeviceWatcherService.ValidateAndBuildDevicesAsync(
            devices,
            oidMapService ?? CreatePassthroughOidMapService(),
            NullLogger.Instance,
            CancellationToken.None);
    }

    // -------------------------------------------------------------------------
    // 1. Valid devices: all fields correct
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidDevices_ReturnsAllDeviceInfos()
    {
        var devices = new List<DeviceOptions>
        {
            new()
            {
                CommunityString = "Simetra.npb-core-01",
                IpAddress = "10.0.10.1",
                Port = 161,
                Polls =
                [
                    new PollOptions { Metrics = [new PollMetricOptions { MetricName = "1.3.6.1.2.1.25.3.3.1.2" }], IntervalSeconds = 30 }
                ]
            },
            new()
            {
                CommunityString = "Simetra.obp-edge-01",
                IpAddress = "10.0.10.2",
                Port = 161,
                Polls = []
            }
        };

        var result = await BuildAsync(devices);

        Assert.Equal(2, result.Count);

        var npb = result.First(d => d.Name == "npb-core-01");
        Assert.Equal("10.0.10.1", npb.ConfigAddress);
        Assert.Equal("10.0.10.1", npb.ResolvedIp);
        Assert.Equal(161, npb.Port);
        Assert.Equal("Simetra.npb-core-01", npb.CommunityString);
        Assert.Single(npb.PollGroups);

        var obp = result.First(d => d.Name == "obp-edge-01");
        Assert.Equal("10.0.10.2", obp.ConfigAddress);
        Assert.Empty(obp.PollGroups);
    }

    // -------------------------------------------------------------------------
    // 2. Invalid CommunityString: skip the device
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvalidCommunityString_SkipsDevice()
    {
        var devices = new List<DeviceOptions>
        {
            new()
            {
                CommunityString = "invalid",
                IpAddress = "10.0.10.1",
                Port = 161,
                Polls = []
            }
        };

        var result = await BuildAsync(devices);

        Assert.Empty(result);
    }

    [Fact]
    public async Task InvalidCommunityString_LogsError()
    {
        var devices = new List<DeviceOptions>
        {
            new()
            {
                CommunityString = "invalid",
                IpAddress = "10.0.10.1",
                Port = 161,
                Polls = []
            }
        };

        var logger = Substitute.For<ILogger>();

        await DeviceWatcherService.ValidateAndBuildDevicesAsync(
            devices,
            CreatePassthroughOidMapService(),
            logger,
            CancellationToken.None);

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("invalid CommunityString")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    // -------------------------------------------------------------------------
    // 3. Duplicate IP+Port: skip the second device
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DuplicateIpPort_SkipsSecondDevice()
    {
        var devices = new List<DeviceOptions>
        {
            new()
            {
                CommunityString = "Simetra.device-a",
                IpAddress = "10.0.10.1",
                Port = 161,
                Polls = []
            },
            new()
            {
                CommunityString = "Simetra.device-b",
                IpAddress = "10.0.10.1",
                Port = 161,
                Polls = []
            }
        };

        var result = await BuildAsync(devices);

        Assert.Single(result);
        Assert.Equal("device-a", result[0].Name);
    }

    [Fact]
    public async Task DuplicateIpPort_LogsError()
    {
        var devices = new List<DeviceOptions>
        {
            new()
            {
                CommunityString = "Simetra.device-a",
                IpAddress = "10.0.10.1",
                Port = 161,
                Polls = []
            },
            new()
            {
                CommunityString = "Simetra.device-b",
                IpAddress = "10.0.10.1",
                Port = 161,
                Polls = []
            }
        };

        var logger = Substitute.For<ILogger>();

        await DeviceWatcherService.ValidateAndBuildDevicesAsync(
            devices,
            CreatePassthroughOidMapService(),
            logger,
            CancellationToken.None);

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("duplicate")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    // -------------------------------------------------------------------------
    // 4. Duplicate CommunityString, different IP+Port: both load with warning
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DuplicateCommunityString_DifferentIpPort_BothLoad()
    {
        var devices = new List<DeviceOptions>
        {
            new()
            {
                CommunityString = "Simetra.shared-cs",
                IpAddress = "10.0.10.1",
                Port = 161,
                Polls = []
            },
            new()
            {
                CommunityString = "Simetra.shared-cs",
                IpAddress = "10.0.10.2",
                Port = 161,
                Polls = []
            }
        };

        var result = await BuildAsync(devices);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task DuplicateCommunityString_DifferentIpPort_LogsWarning()
    {
        var devices = new List<DeviceOptions>
        {
            new()
            {
                CommunityString = "Simetra.shared-cs",
                IpAddress = "10.0.10.1",
                Port = 161,
                Polls = []
            },
            new()
            {
                CommunityString = "Simetra.shared-cs",
                IpAddress = "10.0.10.2",
                Port = 161,
                Polls = []
            }
        };

        var logger = Substitute.For<ILogger>();

        await DeviceWatcherService.ValidateAndBuildDevicesAsync(
            devices,
            CreatePassthroughOidMapService(),
            logger,
            CancellationToken.None);

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("both loaded")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    // -------------------------------------------------------------------------
    // 5. Unresolvable MetricName: excluded from poll group OIDs
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UnresolvableMetricName_ExcludedFromPollGroup()
    {
        var oidMapService = Substitute.For<IOidMapService>();
        oidMapService.ResolveToOid("known").Returns("1.3.6.1.4.1.47477.10.21.1.3.4.0");
        oidMapService.ResolveToOid("unknown").Returns((string?)null);

        var devices = new List<DeviceOptions>
        {
            new()
            {
                CommunityString = "Simetra.test-device",
                IpAddress = "10.0.10.5",
                Port = 161,
                Polls =
                [
                    new PollOptions { Metrics = [new PollMetricOptions { MetricName = "known" }, new PollMetricOptions { MetricName = "unknown" }], IntervalSeconds = 10 }
                ]
            }
        };

        var result = await BuildAsync(devices, oidMapService);

        Assert.Single(result);
        Assert.Single(result[0].PollGroups);
        Assert.Single(result[0].PollGroups[0].Oids);
        Assert.Equal("1.3.6.1.4.1.47477.10.21.1.3.4.0", result[0].PollGroups[0].Oids[0]);
    }

    // -------------------------------------------------------------------------
    // 6. All Metrics unresolvable: poll group excluded, device still in result
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ZeroOidPollGroup_ExcludedFromPollGroups_DeviceStillPresent()
    {
        var oidMapService = Substitute.For<IOidMapService>();
        oidMapService.ResolveToOid(Arg.Any<string>()).Returns((string?)null);

        var devices = new List<DeviceOptions>
        {
            new()
            {
                CommunityString = "Simetra.no-match-device",
                IpAddress = "10.0.10.9",
                Port = 161,
                Polls =
                [
                    new PollOptions { Metrics = [new PollMetricOptions { MetricName = "no_match_1" }, new PollMetricOptions { MetricName = "no_match_2" }], IntervalSeconds = 30 }
                ]
            }
        };

        var result = await BuildAsync(devices, oidMapService);

        // Device still in result (needed for traps)
        Assert.Single(result);
        Assert.Equal("no-match-device", result[0].Name);
        // Poll group filtered out entirely (zero OIDs)
        Assert.Empty(result[0].PollGroups);
    }

    // -------------------------------------------------------------------------
    // 7. Mixed valid and invalid: only valid survive
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MixedValidAndInvalid_OnlyValidSurvive()
    {
        var devices = new List<DeviceOptions>
        {
            new()
            {
                CommunityString = "Simetra.valid-device",
                IpAddress = "10.0.10.1",
                Port = 161,
                Polls = []
            },
            new()
            {
                // Invalid CS -- will be skipped
                CommunityString = "bad-string",
                IpAddress = "10.0.10.2",
                Port = 161,
                Polls = []
            },
            new()
            {
                // Duplicate IP+Port of first device -- will be skipped
                CommunityString = "Simetra.dup-device",
                IpAddress = "10.0.10.1",
                Port = 161,
                Polls = []
            }
        };

        var result = await BuildAsync(devices);

        Assert.Single(result);
        Assert.Equal("valid-device", result[0].Name);
    }

    // -------------------------------------------------------------------------
    // 8. Empty device list: returns empty
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmptyDeviceList_ReturnsEmptyList()
    {
        var result = await BuildAsync(new List<DeviceOptions>());

        Assert.Empty(result);
    }

    // -------------------------------------------------------------------------
    // 9. IP address: normalized to IPv4
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IpAddress_NormalizedToIPv4()
    {
        var devices = new List<DeviceOptions>
        {
            new()
            {
                CommunityString = "Simetra.ip-device",
                IpAddress = "10.0.10.1",
                Port = 161,
                Polls = []
            }
        };

        var result = await BuildAsync(devices);

        Assert.Single(result);
        Assert.Equal("10.0.10.1", result[0].ConfigAddress);
        Assert.Equal("10.0.10.1", result[0].ResolvedIp);
    }

    // -------------------------------------------------------------------------
    // 10. Multiple poll groups: all resolved
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MultiplePollGroups_AllResolved_BothPresent()
    {
        var oidMapService = Substitute.For<IOidMapService>();
        oidMapService.ResolveToOid("metric_a").Returns("1.3.6.1.4.1.1.1.0");
        oidMapService.ResolveToOid("metric_b").Returns("1.3.6.1.4.1.1.2.0");

        var devices = new List<DeviceOptions>
        {
            new()
            {
                CommunityString = "Simetra.multi-poll-device",
                IpAddress = "10.0.10.1",
                Port = 161,
                Polls =
                [
                    new PollOptions { Metrics = [new PollMetricOptions { MetricName = "metric_a" }], IntervalSeconds = 10 },
                    new PollOptions { Metrics = [new PollMetricOptions { MetricName = "metric_b" }], IntervalSeconds = 30 }
                ]
            }
        };

        var result = await BuildAsync(devices, oidMapService);

        Assert.Single(result);
        Assert.Equal(2, result[0].PollGroups.Count);
    }

    // -------------------------------------------------------------------------
    // 11. DNS hostname: resolved to IP
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DnsHostname_ResolvedToIp()
    {
        // "localhost" is universally resolvable to 127.0.0.1
        var devices = new List<DeviceOptions>
        {
            new()
            {
                CommunityString = "Simetra.dns-device",
                IpAddress = "localhost",
                Port = 161,
                Polls = []
            }
        };

        var result = await BuildAsync(devices);

        Assert.Single(result);
        Assert.Equal("localhost", result[0].ConfigAddress);
        Assert.Equal("127.0.0.1", result[0].ResolvedIp);
    }

    // -------------------------------------------------------------------------
    // 12. Zero-OID poll group warning logged
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ZeroOidPollGroup_LogsWarning()
    {
        var oidMapService = Substitute.For<IOidMapService>();
        oidMapService.ResolveToOid(Arg.Any<string>()).Returns((string?)null);

        var devices = new List<DeviceOptions>
        {
            new()
            {
                CommunityString = "Simetra.zero-oid-device",
                IpAddress = "10.0.10.20",
                Port = 161,
                Polls =
                [
                    new PollOptions { Metrics = [new PollMetricOptions { MetricName = "unresolvable_metric" }], IntervalSeconds = 15 }
                ]
            }
        };

        var logger = Substitute.For<ILogger>();

        await DeviceWatcherService.ValidateAndBuildDevicesAsync(
            devices,
            oidMapService,
            logger,
            CancellationToken.None);

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("zero resolved OIDs")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    // -------------------------------------------------------------------------
    // 13-22. Combined metric validation (Phase 38)
    // -------------------------------------------------------------------------

    private static List<DeviceOptions> SingleDeviceWithPoll(PollOptions poll) =>
    [
        new()
        {
            CommunityString = "Simetra.test-device",
            IpAddress = "10.0.10.1",
            Port = 161,
            Polls = [poll]
        }
    ];

    [Fact]
    public async Task AggregatedMetric_ValidConfig_PopulatesAggregatedMetrics()
    {
        var devices = SingleDeviceWithPoll(new PollOptions
        {
            Metrics = [new PollMetricOptions { MetricName = "m1" }, new PollMetricOptions { MetricName = "m2" }],
            AggregatedMetricName = "combined_power",
            Aggregator = "sum",
            IntervalSeconds = 10
        });

        var result = await BuildAsync(devices);

        Assert.Single(result);
        Assert.Single(result[0].PollGroups);
        Assert.Single(result[0].PollGroups[0].AggregatedMetrics);
        Assert.Equal("combined_power", result[0].PollGroups[0].AggregatedMetrics[0].MetricName);
        Assert.Equal(AggregationKind.Sum, result[0].PollGroups[0].AggregatedMetrics[0].Kind);
        Assert.Equal(2, result[0].PollGroups[0].AggregatedMetrics[0].SourceOids.Count);
    }

    [Fact]
    public async Task AggregatedMetric_InvalidAggregator_LogsErrorAndSkipsAggregatedMetric()
    {
        var devices = SingleDeviceWithPoll(new PollOptions
        {
            Metrics = [new PollMetricOptions { MetricName = "m1" }, new PollMetricOptions { MetricName = "m2" }],
            AggregatedMetricName = "combined",
            Aggregator = "invalid",
            IntervalSeconds = 10
        });

        var logger = Substitute.For<ILogger>();

        var result = await DeviceWatcherService.ValidateAndBuildDevicesAsync(
            devices,
            CreatePassthroughOidMapService(),
            logger,
            CancellationToken.None);

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("invalid Aggregator")),
            null,
            Arg.Any<Func<object, Exception?, string>>());

        Assert.Single(result);
        Assert.Single(result[0].PollGroups);
        Assert.Empty(result[0].PollGroups[0].AggregatedMetrics);
    }

    [Fact]
    public async Task AggregatedMetric_MissingAggregator_LogsErrorAndSkipsAggregatedMetric()
    {
        var devices = SingleDeviceWithPoll(new PollOptions
        {
            Metrics = [new PollMetricOptions { MetricName = "m1" }, new PollMetricOptions { MetricName = "m2" }],
            AggregatedMetricName = "combined",
            Aggregator = null,
            IntervalSeconds = 10
        });

        var logger = Substitute.For<ILogger>();

        var result = await DeviceWatcherService.ValidateAndBuildDevicesAsync(
            devices,
            CreatePassthroughOidMapService(),
            logger,
            CancellationToken.None);

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Aggregator missing")),
            null,
            Arg.Any<Func<object, Exception?, string>>());

        Assert.Empty(result[0].PollGroups[0].AggregatedMetrics);
    }

    [Fact]
    public async Task AggregatedMetric_MissingName_LogsErrorAndSkipsAggregatedMetric()
    {
        var devices = SingleDeviceWithPoll(new PollOptions
        {
            Metrics = [new PollMetricOptions { MetricName = "m1" }, new PollMetricOptions { MetricName = "m2" }],
            AggregatedMetricName = null,
            Aggregator = "sum",
            IntervalSeconds = 10
        });

        var logger = Substitute.For<ILogger>();

        var result = await DeviceWatcherService.ValidateAndBuildDevicesAsync(
            devices,
            CreatePassthroughOidMapService(),
            logger,
            CancellationToken.None);

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("AggregatedMetricName missing")),
            null,
            Arg.Any<Func<object, Exception?, string>>());

        Assert.Empty(result[0].PollGroups[0].AggregatedMetrics);
    }

    [Fact]
    public async Task AggregatedMetric_FewerThan2ResolvedOids_LogsErrorAndSkipsAggregatedMetric()
    {
        var devices = SingleDeviceWithPoll(new PollOptions
        {
            Metrics = [new PollMetricOptions { MetricName = "m1" }],
            AggregatedMetricName = "combined",
            Aggregator = "sum",
            IntervalSeconds = 10
        });

        var logger = Substitute.For<ILogger>();

        var result = await DeviceWatcherService.ValidateAndBuildDevicesAsync(
            devices,
            CreatePassthroughOidMapService(),
            logger,
            CancellationToken.None);

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("fewer than 2 resolved OIDs")),
            null,
            Arg.Any<Func<object, Exception?, string>>());

        Assert.Empty(result[0].PollGroups[0].AggregatedMetrics);
    }

    [Fact]
    public async Task AggregatedMetric_DuplicateNameOnSameDevice_LogsErrorAndSkipsSecond()
    {
        var poll1 = new PollOptions
        {
            Metrics = [new PollMetricOptions { MetricName = "m1" }, new PollMetricOptions { MetricName = "m2" }],
            AggregatedMetricName = "combined",
            Aggregator = "sum",
            IntervalSeconds = 10
        };
        var poll2 = new PollOptions
        {
            Metrics = [new PollMetricOptions { MetricName = "m1" }, new PollMetricOptions { MetricName = "m2" }],
            AggregatedMetricName = "combined",
            Aggregator = "sum",
            IntervalSeconds = 10
        };

        var devices = new List<DeviceOptions>
        {
            new()
            {
                CommunityString = "Simetra.test-device",
                IpAddress = "10.0.10.1",
                Port = 161,
                Polls = [poll1, poll2]
            }
        };

        var logger = Substitute.For<ILogger>();

        var result = await DeviceWatcherService.ValidateAndBuildDevicesAsync(
            devices,
            CreatePassthroughOidMapService(),
            logger,
            CancellationToken.None);

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("duplicate AggregatedMetricName")),
            null,
            Arg.Any<Func<object, Exception?, string>>());

        Assert.Equal(2, result[0].PollGroups.Count);
        Assert.Single(result[0].PollGroups[0].AggregatedMetrics);
        Assert.Empty(result[0].PollGroups[1].AggregatedMetrics);
    }

    [Fact]
    public async Task AggregatedMetric_OidMapCollision_LogsErrorAndSkipsAggregatedMetric()
    {
        var svc = CreatePassthroughOidMapService();
        svc.ContainsMetricName("colliding_name").Returns(true);

        var devices = SingleDeviceWithPoll(new PollOptions
        {
            Metrics = [new PollMetricOptions { MetricName = "m1" }, new PollMetricOptions { MetricName = "m2" }],
            AggregatedMetricName = "colliding_name",
            Aggregator = "sum",
            IntervalSeconds = 10
        });

        var logger = Substitute.For<ILogger>();

        var result = await DeviceWatcherService.ValidateAndBuildDevicesAsync(
            devices,
            svc,
            logger,
            CancellationToken.None);

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("collides with existing OID map entry")),
            null,
            Arg.Any<Func<object, Exception?, string>>());

        Assert.Empty(result[0].PollGroups[0].AggregatedMetrics);
    }

    [Fact]
    public async Task AggregatedMetric_NeitherFieldSet_NoAggregatedMetricNoError()
    {
        var devices = SingleDeviceWithPoll(new PollOptions
        {
            Metrics = [new PollMetricOptions { MetricName = "m1" }, new PollMetricOptions { MetricName = "m2" }],
            AggregatedMetricName = null,
            Aggregator = null,
            IntervalSeconds = 10
        });

        var logger = Substitute.For<ILogger>();

        var result = await DeviceWatcherService.ValidateAndBuildDevicesAsync(
            devices,
            CreatePassthroughOidMapService(),
            logger,
            CancellationToken.None);

        Assert.Empty(result[0].PollGroups[0].AggregatedMetrics);

        logger.DidNotReceive().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Theory]
    [InlineData("sum", AggregationKind.Sum)]
    [InlineData("subtract", AggregationKind.Subtract)]
    [InlineData("absDiff", AggregationKind.AbsDiff)]
    [InlineData("mean", AggregationKind.Mean)]
    public async Task AggregatedMetric_AllFourAggregatorValues_ParseCorrectly(string aggregator, AggregationKind expectedKind)
    {
        var devices = SingleDeviceWithPoll(new PollOptions
        {
            Metrics = [new PollMetricOptions { MetricName = "m1" }, new PollMetricOptions { MetricName = "m2" }],
            AggregatedMetricName = "combined",
            Aggregator = aggregator,
            IntervalSeconds = 10
        });

        var result = await BuildAsync(devices);

        Assert.Single(result[0].PollGroups[0].AggregatedMetrics);
        Assert.Equal(expectedKind, result[0].PollGroups[0].AggregatedMetrics[0].Kind);
    }

    [Fact]
    public async Task AggregatedMetric_InvalidAggregatedMetric_PollGroupStillLoadsIndividualOids()
    {
        var devices = SingleDeviceWithPoll(new PollOptions
        {
            Metrics = [new PollMetricOptions { MetricName = "m1" }, new PollMetricOptions { MetricName = "m2" }],
            AggregatedMetricName = "combined",
            Aggregator = "invalid",
            IntervalSeconds = 10
        });

        var result = await BuildAsync(devices);

        Assert.Single(result);
        Assert.Single(result[0].PollGroups);
        Assert.Equal(2, result[0].PollGroups[0].Oids.Count);
        Assert.Empty(result[0].PollGroups[0].AggregatedMetrics);
    }
}
