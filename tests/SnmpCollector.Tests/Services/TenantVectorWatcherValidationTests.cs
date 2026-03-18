using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using SnmpCollector.Services;
using Xunit;

namespace SnmpCollector.Tests.Services;

public sealed class TenantVectorWatcherValidationTests
{
    // ──────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────

    /// <summary>Default snapshot interval used in all test calls to ValidateAndBuildTenants.</summary>
    private const int TestSnapshotIntervalSeconds = 15;

    /// <summary>Passthrough OID used by the default device registry poll group.</summary>
    private const string PassthroughOid = "1.3.6.1.99";

    private static IOidMapService CreatePassthroughOidMapService()
    {
        var svc = Substitute.For<IOidMapService>();
        svc.ContainsMetricName(Arg.Any<string>()).Returns(true);
        svc.ResolveToOid(Arg.Any<string>()).Returns(PassthroughOid);
        return svc;
    }

    private static IDeviceRegistry CreatePassthroughDeviceRegistry(string ip = "10.0.0.1")
    {
        var pollGroup = new MetricPollInfo(
            PollIndex: 0,
            Oids: new[] { PassthroughOid }.AsReadOnly(),
            IntervalSeconds: 10,
            GraceMultiplier: 2.0);

        var device = new DeviceInfo(
            Name: "passthrough",
            ConfigAddress: ip,
            ResolvedIp: ip,
            Port: 161,
            PollGroups: new[] { pollGroup },
            CommunityString: "Simetra.passthrough");

        var reg = Substitute.For<IDeviceRegistry>();
        reg.TryGetByIpPort(Arg.Any<string>(), Arg.Any<int>(), out Arg.Any<DeviceInfo?>())
            .Returns(x => { x[2] = device; return true; });
        reg.AllDevices.Returns(new[] { device });
        return reg;
    }

    /// <summary>
    /// Builds a minimal valid tenant that passes TEN-13 (has Evaluate, Resolved, and command).
    /// </summary>
    private static TenantOptions CreateValidTenant(string ip = "10.0.0.1", int port = 161) => new()
    {
        Priority = 1,
        Metrics = new List<MetricSlotOptions>
        {
            new() { Ip = ip, Port = port, MetricName = "m1", Role = "Evaluate" },
            new() { Ip = ip, Port = port, MetricName = "m2", Role = "Resolved" }
        },
        Commands = new List<CommandSlotOptions>
        {
            new() { Ip = ip, Port = port, CommandName = "cmd1", Value = "100", ValueType = "Integer32" }
        }
    };

    private static TenantVectorOptions Wrap(TenantOptions tenant) => new()
    {
        Tenants = new List<TenantOptions> { tenant }
    };

    // ──────────────────────────────────────────────────────
    // Metric validation tests
    // ──────────────────────────────────────────────────────

    [Fact]
    public void ValidMetrics_AllSurvive()
    {
        var options = Wrap(CreateValidTenant());
        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            options, CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count);
        Assert.Single(result.Tenants[0].Commands);
    }

    [Fact]
    public void EmptyIp_MetricSkipped()
    {
        // Tenant has 3 metrics: 1 with empty IP (invalid), 1 Evaluate, 1 Resolved — TEN-13 still passes.
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "", Port = 161, MetricName = "bad", Role = "Evaluate" },       // invalid
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" }, // valid
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }  // valid
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count); // only the 2 valid entries
    }

    [Fact]
    public void PortOutOfRange_MetricSkipped()
    {
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 0, MetricName = "bad", Role = "Evaluate" },    // port=0 invalid
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count);
    }

    [Fact]
    public void EmptyMetricName_MetricSkipped()
    {
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "", Role = "Evaluate" },     // empty name
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count);
    }

    [Fact]
    public void InvalidRole_MetricSkipped()
    {
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "bad", Role = "Other" },     // invalid role
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count);
    }

    [Fact]
    public void MetricNameNotInOidMap_MetricSkipped()
    {
        // TEN-05: OidMapService returns false for "unknownMetric".
        var oidMap = Substitute.For<IOidMapService>();
        oidMap.ContainsMetricName("unknownMetric").Returns(false);
        oidMap.ContainsMetricName(Arg.Is<string>(s => s != "unknownMetric")).Returns(true);
        oidMap.ResolveToOid(Arg.Any<string>()).Returns(PassthroughOid);

        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "unknownMetric", Role = "Evaluate" }, // not in OID map
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), oidMap, CreatePassthroughDeviceRegistry(), TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count);
    }

    [Fact]
    public void MetricNameNotInOidMap_ButIsAggregateMetric_MetricSurvives()
    {
        // TEN-05 fallback: MetricName not in OID map but is an AggregatedMetricName in device poll groups.
        var oidMap = Substitute.For<IOidMapService>();
        oidMap.ContainsMetricName("e2e_total_util").Returns(false);
        oidMap.ContainsMetricName(Arg.Is<string>(s => s != "e2e_total_util")).Returns(true);
        // e2e_total_util has no OID — resolved via aggregate fallback. m2 resolves to .1.1 (in poll group).
        oidMap.ResolveToOid("e2e_total_util").Returns((string?)null);
        oidMap.ResolveToOid("m2").Returns(".1.1");

        var aggDef = new AggregatedMetricDefinition("e2e_total_util", AggregationKind.Sum, new[] { ".1.1", ".1.2" });
        var pollInfo = new MetricPollInfo(0, new[] { ".1.1", ".1.2" }, 10)
        {
            AggregatedMetrics = new[] { aggDef }
        };
        var deviceInfo = new DeviceInfo("E2E-SIM", "10.0.0.1", "10.0.0.1", 161, new[] { pollInfo }, "Simetra.E2E-SIM");

        var reg = Substitute.For<IDeviceRegistry>();
        reg.TryGetByIpPort("10.0.0.1", 161, out Arg.Any<DeviceInfo?>())
            .Returns(x => { x[2] = deviceInfo; return true; });
        reg.AllDevices.Returns(new[] { deviceInfo });

        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "e2e_total_util", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), oidMap, reg, TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count);
        Assert.Equal("e2e_total_util", result.Tenants[0].Metrics[0].MetricName);
    }

    [Fact]
    public void TimeSeriesSizeZero_MetricSkipped()
    {
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "bad", Role = "Evaluate", TimeSeriesSize = 0 }, // invalid
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count);
    }

    [Fact]
    public void TimeSeriesSizeNegative_MetricSkipped()
    {
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "bad", Role = "Evaluate", TimeSeriesSize = -1 }, // invalid
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count);
    }

    [Fact]
    public void IpPortNotInDeviceRegistry_MetricSkipped()
    {
        // TEN-07: DeviceRegistry returns false for "10.0.0.99".
        var pollGroup = new MetricPollInfo(0, new[] { PassthroughOid }.AsReadOnly(), 10, GraceMultiplier: 2.0);
        var device = new DeviceInfo("dev", "10.0.0.1", "10.0.0.1", 161, new[] { pollGroup }, "Simetra.dev");

        var devReg = Substitute.For<IDeviceRegistry>();
        devReg.AllDevices.Returns(new[] { device });
        devReg.TryGetByIpPort("10.0.0.99", Arg.Any<int>(), out Arg.Any<DeviceInfo?>()).Returns(false);
        devReg.TryGetByIpPort(Arg.Is<string>(s => s != "10.0.0.99"), Arg.Any<int>(), out Arg.Any<DeviceInfo?>())
            .Returns(x => { x[2] = device; return true; });

        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.99", Port = 161, MetricName = "unknown_device", Role = "Evaluate" }, // not in registry
                new() { Ip = "10.0.0.1",  Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1",  Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), devReg, TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count);
    }

    // ──────────────────────────────────────────────────────
    // Command validation tests
    // ──────────────────────────────────────────────────────

    [Fact]
    public void ValidCommands_AllSurvive()
    {
        var options = Wrap(CreateValidTenant());
        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            options, CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Single(result.Tenants[0].Commands);
    }

    [Fact]
    public void EmptyCommandName_CommandSkipped()
    {
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "", Value = "1", ValueType = "Integer32" },          // empty name
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "good-cmd", Value = "1", ValueType = "Integer32" }  // valid
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Single(result.Tenants[0].Commands); // only valid command
    }

    [Fact]
    public void InvalidValueType_CommandSkipped()
    {
        // TEN-03: "Float" is not a valid ValueType.
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "bad-cmd", Value = "1.5", ValueType = "Float" },    // invalid
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "good-cmd", Value = "1", ValueType = "Integer32" } // valid
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Single(result.Tenants[0].Commands);
    }

    [Fact]
    public void EmptyValue_CommandSkipped()
    {
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "empty-val", Value = "", ValueType = "Integer32" },  // empty value
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "good-cmd", Value = "1", ValueType = "Integer32" }  // valid
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Single(result.Tenants[0].Commands);
    }

    [Fact]
    public void IpPortNotInDeviceRegistry_CommandSkipped()
    {
        // TEN-07 for commands: DeviceRegistry returns false for "10.0.0.99".
        var pollGroup = new MetricPollInfo(0, new[] { PassthroughOid }.AsReadOnly(), 10, GraceMultiplier: 2.0);
        var device = new DeviceInfo("dev", "10.0.0.1", "10.0.0.1", 161, new[] { pollGroup }, "Simetra.dev");

        var devReg = Substitute.For<IDeviceRegistry>();
        devReg.AllDevices.Returns(new[] { device });
        devReg.TryGetByIpPort("10.0.0.99", Arg.Any<int>(), out Arg.Any<DeviceInfo?>()).Returns(false);
        devReg.TryGetByIpPort(Arg.Is<string>(s => s != "10.0.0.99"), Arg.Any<int>(), out Arg.Any<DeviceInfo?>())
            .Returns(x => { x[2] = device; return true; });

        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.99", Port = 161, CommandName = "bad-cmd", Value = "1", ValueType = "Integer32" }, // not in registry
                new() { Ip = "10.0.0.1",  Port = 161, CommandName = "good-cmd", Value = "1", ValueType = "Integer32" } // valid
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), devReg, TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Single(result.Tenants[0].Commands);
    }

    // ──────────────────────────────────────────────────────
    // Value+ValueType parse validation tests
    // ──────────────────────────────────────────────────────

    [Fact]
    public void Command_Integer32_InvalidValue_Skipped()
    {
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "bad-int", Value = "not-a-number", ValueType = "Integer32" },  // invalid
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "good-cmd", Value = "42", ValueType = "Integer32" }            // valid
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Single(result.Tenants[0].Commands);
        Assert.Equal("good-cmd", result.Tenants[0].Commands[0].CommandName);
    }

    [Fact]
    public void Command_IpAddress_InvalidValue_Skipped()
    {
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "bad-ip", Value = "not-an-ip", ValueType = "IpAddress" },     // invalid
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "good-cmd", Value = "42", ValueType = "Integer32" }           // valid
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Single(result.Tenants[0].Commands);
        Assert.Equal("good-cmd", result.Tenants[0].Commands[0].CommandName);
    }

    [Fact]
    public void Command_OctetString_AnyValue_Accepted()
    {
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "octet-cmd", Value = "anything goes here!", ValueType = "OctetString" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Single(result.Tenants[0].Commands);
        Assert.Equal("octet-cmd", result.Tenants[0].Commands[0].CommandName);
    }

    [Fact]
    public void Command_Integer32_ValidValue_Accepted()
    {
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "int-cmd", Value = "42", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Single(result.Tenants[0].Commands);
        Assert.Equal("int-cmd", result.Tenants[0].Commands[0].CommandName);
    }

    // ──────────────────────────────────────────────────────
    // TEN-06: CommandName pass-through
    // ──────────────────────────────────────────────────────

    [Fact]
    public void UnresolvableCommandName_StoredAsIs()
    {
        // TEN-06: watcher does NOT check if CommandName exists in command map.
        // Any non-empty CommandName that passes structural + TEN-07 checks is stored as-is.
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "no-such-command-in-map", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Single(result.Tenants[0].Commands);
        Assert.Equal("no-such-command-in-map", result.Tenants[0].Commands[0].CommandName);
    }

    // ──────────────────────────────────────────────────────
    // TEN-13 gate tests
    // ──────────────────────────────────────────────────────

    [Fact]
    public void NoResolvedMetrics_TenantSkipped()
    {
        var tenant = new TenantOptions
        {
            Priority = 1,
            // Only Evaluate metrics — no Resolved metric.
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Evaluate" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Empty(result.Tenants);
    }

    [Fact]
    public void NoEvaluateMetrics_TenantSkipped()
    {
        var tenant = new TenantOptions
        {
            Priority = 1,
            // Only Resolved metrics — no Evaluate metric.
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Resolved" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Empty(result.Tenants);
    }

    [Fact]
    public void NoCommands_TenantSkipped()
    {
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>() // no commands
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Empty(result.Tenants);
    }

    // ──────────────────────────────────────────────────────
    // IP resolution test
    // ──────────────────────────────────────────────────────

    [Fact]
    public void IpResolved_ViaDeviceRegistryAllDevices()
    {
        // DeviceInfo with ConfigAddress="my-host" and ResolvedIp="10.0.0.5".
        var pollGroup = new MetricPollInfo(0, new[] { PassthroughOid }.AsReadOnly(), 10, GraceMultiplier: 2.0);
        var device = new DeviceInfo(
            Name: "my-host",
            ConfigAddress: "my-host",
            ResolvedIp: "10.0.0.5",
            Port: 161,
            PollGroups: new[] { pollGroup },
            CommunityString: "Simetra.my-host");

        var devReg = Substitute.For<IDeviceRegistry>();
        devReg.AllDevices.Returns(new[] { device });
        devReg.TryGetByIpPort(Arg.Any<string>(), Arg.Any<int>(), out Arg.Any<DeviceInfo?>())
            .Returns(x => { x[2] = device; return true; });

        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "my-host", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "my-host", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "my-host", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), devReg, TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        // Metrics IPs are resolved to "10.0.0.5".
        Assert.All(result.Tenants[0].Metrics, m => Assert.Equal("10.0.0.5", m.Ip));
    }

    // ──────────────────────────────────────────────────────
    // Edge case tests
    // ──────────────────────────────────────────────────────

    [Fact]
    public void EmptyTenants_ReturnsEmptyOptions()
    {
        var options = new TenantVectorOptions { Tenants = new List<TenantOptions>() };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            options, CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Empty(result.Tenants);
    }

    [Fact]
    public void MixedValidAndInvalid_OnlyValidSurvive()
    {
        // Tenant with 3 metrics (1 invalid: empty IP) + 2 commands (1 invalid: empty CommandName).
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "", Port = 161, MetricName = "bad-metric", Role = "Evaluate" },      // invalid
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },      // valid
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }       // valid
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "", Value = "1", ValueType = "Integer32" },      // invalid
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "good-cmd", Value = "1", ValueType = "Integer32" } // valid
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count);   // 2 valid metrics
        Assert.Single(result.Tenants[0].Commands);           // 1 valid command
    }

    // ──────────────────────────────────────────────────────
    // Threshold validation tests (THR-04/THR-05/THR-06)
    // ──────────────────────────────────────────────────────

    [Fact]
    public void ValidThreshold_PreservedOnCleanMetric()
    {
        var tenant = CreateValidTenant();
        tenant.Metrics[0].Threshold = new ThresholdOptions { Min = 10.0, Max = 90.0 };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count);
        var th = result.Tenants[0].Metrics[0].Threshold;
        Assert.NotNull(th);
        Assert.Equal(10.0, th.Min);
        Assert.Equal(90.0, th.Max);
    }

    [Fact]
    public void MinGreaterThanMax_MetricSkipped()
    {
        // Threshold Min > Max now SKIPS the metric (was: clear threshold + keep metric).
        // Need 2 Evaluate + 1 Resolved so TEN-13 still passes after 1 Evaluate is skipped.
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "bad", Role = "Evaluate",
                    Threshold = new ThresholdOptions { Min = 100.0, Max = 50.0 } }, // skipped
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count); // bad metric skipped, 2 remain
    }

    [Fact]
    public void BothNullThreshold_IsValid_PassesThrough()
    {
        var tenant = CreateValidTenant();
        tenant.Metrics[0].Threshold = new ThresholdOptions { Min = null, Max = null };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count);
        Assert.NotNull(result.Tenants[0].Metrics[0].Threshold); // threshold object preserved (always-violated)
    }

    // ──────────────────────────────────────────────────────
    // IntervalSeconds + GraceMultiplier resolution tests
    // ──────────────────────────────────────────────────────

    [Fact]
    public void IntervalSeconds_ResolvedFromDevicePollGroup()
    {
        // Poll group contains OID "1.2.3" at IntervalSeconds=10.
        var pollGroup = new MetricPollInfo(
            PollIndex: 0,
            Oids: new[] { "1.2.3" }.AsReadOnly(),
            IntervalSeconds: 10,
            GraceMultiplier: 2.0);

        var device = new DeviceInfo(
            Name: "dev",
            ConfigAddress: "10.0.0.1",
            ResolvedIp: "10.0.0.1",
            Port: 161,
            PollGroups: new[] { pollGroup },
            CommunityString: "Simetra.dev");

        var oidMap = Substitute.For<IOidMapService>();
        oidMap.ContainsMetricName(Arg.Any<string>()).Returns(true);
        oidMap.ResolveToOid("m1").Returns("1.2.3");
        oidMap.ResolveToOid("m2").Returns("1.2.3");

        var devReg = Substitute.For<IDeviceRegistry>();
        devReg.TryGetByIpPort(Arg.Any<string>(), Arg.Any<int>(), out Arg.Any<DeviceInfo?>())
            .Returns(x => { x[2] = device; return true; });
        devReg.AllDevices.Returns(new[] { device });

        var tenant = CreateValidTenant();
        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), oidMap, devReg, TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(10, result.Tenants[0].Metrics[0].IntervalSeconds);
    }

    [Fact]
    public void GraceMultiplier_ResolvedFromDevicePollGroup()
    {
        // Poll group contains OID "1.2.3" at GraceMultiplier=3.0.
        var pollGroup = new MetricPollInfo(
            PollIndex: 0,
            Oids: new[] { "1.2.3" }.AsReadOnly(),
            IntervalSeconds: 10,
            GraceMultiplier: 3.0);

        var device = new DeviceInfo(
            Name: "dev",
            ConfigAddress: "10.0.0.1",
            ResolvedIp: "10.0.0.1",
            Port: 161,
            PollGroups: new[] { pollGroup },
            CommunityString: "Simetra.dev");

        var oidMap = Substitute.For<IOidMapService>();
        oidMap.ContainsMetricName(Arg.Any<string>()).Returns(true);
        oidMap.ResolveToOid("m1").Returns("1.2.3");
        oidMap.ResolveToOid("m2").Returns("1.2.3");

        var devReg = Substitute.For<IDeviceRegistry>();
        devReg.TryGetByIpPort(Arg.Any<string>(), Arg.Any<int>(), out Arg.Any<DeviceInfo?>())
            .Returns(x => { x[2] = device; return true; });
        devReg.AllDevices.Returns(new[] { device });

        var tenant = CreateValidTenant();
        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), oidMap, devReg, TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(3.0, result.Tenants[0].Metrics[0].GraceMultiplier);
    }

    [Fact]
    public void MetricNameNotInAnyPollGroup_MetricSkipped()
    {
        // Device exists but OID "9.9.9" is not in any poll group.
        // IntervalSeconds stays 0, which now causes the metric to be skipped.
        var pollGroup = new MetricPollInfo(
            PollIndex: 0,
            Oids: new[] { "1.2.3" }.AsReadOnly(),
            IntervalSeconds: 10,
            GraceMultiplier: 2.0);

        var device = new DeviceInfo(
            Name: "dev",
            ConfigAddress: "10.0.0.1",
            ResolvedIp: "10.0.0.1",
            Port: 161,
            PollGroups: new[] { pollGroup },
            CommunityString: "Simetra.dev");

        var oidMap = Substitute.For<IOidMapService>();
        oidMap.ContainsMetricName(Arg.Any<string>()).Returns(true);
        // Both metrics resolve to OIDs not in any poll group.
        oidMap.ResolveToOid(Arg.Any<string>()).Returns("9.9.9");

        var devReg = Substitute.For<IDeviceRegistry>();
        devReg.TryGetByIpPort(Arg.Any<string>(), Arg.Any<int>(), out Arg.Any<DeviceInfo?>())
            .Returns(x => { x[2] = device; return true; });
        devReg.AllDevices.Returns(new[] { device });

        var tenant = CreateValidTenant();
        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), oidMap, devReg, TestSnapshotIntervalSeconds, NullLogger.Instance);

        // All metrics skipped (IntervalSeconds=0) -> TEN-13 fails -> tenant skipped
        Assert.Empty(result.Tenants);
    }

    [Fact]
    public void AggregatedMetricName_ResolvedFromPollGroup()
    {
        // Poll group has AggregatedMetrics containing "agg_metric" at IntervalSeconds=15.
        var aggDef = new AggregatedMetricDefinition(
            MetricName: "agg_metric",
            Kind: AggregationKind.Sum,
            SourceOids: Array.Empty<string>());

        var pollGroup = new MetricPollInfo(
            PollIndex: 0,
            Oids: new[] { "1.2.3" }.AsReadOnly(),
            IntervalSeconds: 15,
            GraceMultiplier: 2.0)
        {
            AggregatedMetrics = new[] { aggDef }
        };

        var device = new DeviceInfo(
            Name: "dev",
            ConfigAddress: "10.0.0.1",
            ResolvedIp: "10.0.0.1",
            Port: 161,
            PollGroups: new[] { pollGroup },
            CommunityString: "Simetra.dev");

        var oidMap = Substitute.For<IOidMapService>();
        oidMap.ContainsMetricName(Arg.Any<string>()).Returns(true);
        // agg_metric has no OID entry — ResolveToOid returns null, triggering fallback.
        oidMap.ResolveToOid("agg_metric").Returns((string?)null);
        oidMap.ResolveToOid("m2").Returns("1.2.3");

        var devReg = Substitute.For<IDeviceRegistry>();
        devReg.TryGetByIpPort(Arg.Any<string>(), Arg.Any<int>(), out Arg.Any<DeviceInfo?>())
            .Returns(x => { x[2] = device; return true; });
        devReg.AllDevices.Returns(new[] { device });

        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "agg_metric", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), oidMap, devReg, TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(15, result.Tenants[0].Metrics[0].IntervalSeconds);
    }

    // ──────────────────────────────────────────────────────
    // Plan 56-01: New validation tests
    // ──────────────────────────────────────────────────────

    [Fact]
    public void ThresholdMinGreaterThanMax_MetricSkipped()
    {
        // Metric with Min=100, Max=50 is skipped (not loaded with null threshold).
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "thr-bad", Role = "Evaluate",
                    Threshold = new ThresholdOptions { Min = 100.0, Max = 50.0 } }, // Min > Max: skipped
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count); // thr-bad skipped, 2 remain
    }

    [Fact]
    public void TimeSeriesSizeExceedsMax_MetricSkipped()
    {
        // Metric with TimeSeriesSize=1001 is skipped.
        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "too-big", Role = "Evaluate", TimeSeriesSize = 1001 }, // exceeds 1000
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count); // too-big skipped
    }

    [Fact]
    public void IpNotResolved_MetricSkipped()
    {
        // Metric with hostname IP where AllDevices has no matching ConfigAddress.
        // DeviceRegistry returns true for TryGetByIpPort (device exists) but AllDevices
        // doesn't contain "my-unresolvable-host", so IP stays as hostname (not a valid IP).
        var pollGroup = new MetricPollInfo(0, new[] { PassthroughOid }.AsReadOnly(), 10, GraceMultiplier: 2.0);
        var device = new DeviceInfo("dev", "10.0.0.1", "10.0.0.1", 161, new[] { pollGroup }, "Simetra.dev");

        var devReg = Substitute.For<IDeviceRegistry>();
        devReg.TryGetByIpPort(Arg.Any<string>(), Arg.Any<int>(), out Arg.Any<DeviceInfo?>())
            .Returns(x => { x[2] = device; return true; });
        devReg.AllDevices.Returns(new[] { device }); // only "10.0.0.1", not "my-unresolvable-host"

        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "my-unresolvable-host", Port = 161, MetricName = "bad", Role = "Evaluate" }, // hostname, not in AllDevices
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), devReg, TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count); // unresolvable hostname skipped
    }

    [Fact]
    public void IntervalSecondsZero_MetricSkipped()
    {
        // Metric that can't resolve IntervalSeconds (OID not in poll group, not an aggregate).
        var pollGroup = new MetricPollInfo(0, new[] { "1.2.3" }.AsReadOnly(), 10, GraceMultiplier: 2.0);
        var device = new DeviceInfo("dev", "10.0.0.1", "10.0.0.1", 161, new[] { pollGroup }, "Simetra.dev");

        var oidMap = Substitute.For<IOidMapService>();
        oidMap.ContainsMetricName(Arg.Any<string>()).Returns(true);
        oidMap.ResolveToOid("bad-interval").Returns("9.9.9"); // not in poll group
        oidMap.ResolveToOid("m1").Returns("1.2.3");
        oidMap.ResolveToOid("m2").Returns("1.2.3");

        var devReg = Substitute.For<IDeviceRegistry>();
        devReg.TryGetByIpPort(Arg.Any<string>(), Arg.Any<int>(), out Arg.Any<DeviceInfo?>())
            .Returns(x => { x[2] = device; return true; });
        devReg.AllDevices.Returns(new[] { device });

        var tenant = new TenantOptions
        {
            Priority = 1,
            Metrics = new List<MetricSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "bad-interval", Role = "Evaluate" }, // interval=0: skipped
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m1", Role = "Evaluate" },
                new() { Ip = "10.0.0.1", Port = 161, MetricName = "m2", Role = "Resolved" }
            },
            Commands = new List<CommandSlotOptions>
            {
                new() { Ip = "10.0.0.1", Port = 161, CommandName = "cmd1", Value = "1", ValueType = "Integer32" }
            }
        };

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), oidMap, devReg, TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count); // bad-interval skipped
    }

    [Fact]
    public void SuppressionWindowZero_ClampedToInterval()
    {
        var tenant = CreateValidTenant();
        tenant.SuppressionWindowSeconds = 0;

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(TestSnapshotIntervalSeconds, result.Tenants[0].SuppressionWindowSeconds);
    }

    [Fact]
    public void SuppressionWindowNegativeOne_AcceptedAsDisabled()
    {
        var tenant = CreateValidTenant();
        tenant.SuppressionWindowSeconds = -1;

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(-1, result.Tenants[0].SuppressionWindowSeconds);
    }

    [Fact]
    public void SuppressionWindowBelowInterval_ClampedToInterval()
    {
        var tenant = CreateValidTenant();
        tenant.SuppressionWindowSeconds = 5; // below TestSnapshotIntervalSeconds (15)

        var result = TenantVectorWatcherService.ValidateAndBuildTenants(
            Wrap(tenant), CreatePassthroughOidMapService(), CreatePassthroughDeviceRegistry(),
            TestSnapshotIntervalSeconds, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(TestSnapshotIntervalSeconds, result.Tenants[0].SuppressionWindowSeconds);
    }
}
