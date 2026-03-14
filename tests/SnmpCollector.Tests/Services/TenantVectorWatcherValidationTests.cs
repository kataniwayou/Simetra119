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

    private static IOidMapService CreatePassthroughOidMapService()
    {
        var svc = Substitute.For<IOidMapService>();
        svc.ContainsMetricName(Arg.Any<string>()).Returns(true);
        return svc;
    }

    private static IDeviceRegistry CreatePassthroughDeviceRegistry()
    {
        var reg = Substitute.For<IDeviceRegistry>();
        reg.TryGetByIpPort(Arg.Any<string>(), Arg.Any<int>(), out Arg.Any<DeviceInfo?>())
            .Returns(true);
        reg.AllDevices.Returns(Array.Empty<DeviceInfo>());
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
            NullLogger.Instance);

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
            NullLogger.Instance);

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
            NullLogger.Instance);

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
            NullLogger.Instance);

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
            NullLogger.Instance);

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
            Wrap(tenant), oidMap, CreatePassthroughDeviceRegistry(), NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count);
    }

    [Fact]
    public void IpPortNotInDeviceRegistry_MetricSkipped()
    {
        // TEN-07: DeviceRegistry returns false for "10.0.0.99".
        var devReg = Substitute.For<IDeviceRegistry>();
        devReg.AllDevices.Returns(Array.Empty<DeviceInfo>());
        devReg.TryGetByIpPort("10.0.0.99", Arg.Any<int>(), out Arg.Any<DeviceInfo?>()).Returns(false);
        devReg.TryGetByIpPort(Arg.Is<string>(s => s != "10.0.0.99"), Arg.Any<int>(), out Arg.Any<DeviceInfo?>()).Returns(true);

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
            Wrap(tenant), CreatePassthroughOidMapService(), devReg, NullLogger.Instance);

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
            NullLogger.Instance);

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
            NullLogger.Instance);

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
            NullLogger.Instance);

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
            NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Single(result.Tenants[0].Commands);
    }

    [Fact]
    public void IpPortNotInDeviceRegistry_CommandSkipped()
    {
        // TEN-07 for commands: DeviceRegistry returns false for "10.0.0.99".
        var devReg = Substitute.For<IDeviceRegistry>();
        devReg.AllDevices.Returns(Array.Empty<DeviceInfo>());
        devReg.TryGetByIpPort("10.0.0.99", Arg.Any<int>(), out Arg.Any<DeviceInfo?>()).Returns(false);
        devReg.TryGetByIpPort(Arg.Is<string>(s => s != "10.0.0.99"), Arg.Any<int>(), out Arg.Any<DeviceInfo?>()).Returns(true);

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
            Wrap(tenant), CreatePassthroughOidMapService(), devReg, NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Single(result.Tenants[0].Commands);
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
            NullLogger.Instance);

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
            NullLogger.Instance);

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
            NullLogger.Instance);

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
            NullLogger.Instance);

        Assert.Empty(result.Tenants);
    }

    // ──────────────────────────────────────────────────────
    // IP resolution test
    // ──────────────────────────────────────────────────────

    [Fact]
    public void IpResolved_ViaDeviceRegistryAllDevices()
    {
        // DeviceInfo with ConfigAddress="my-host" and ResolvedIp="10.0.0.5".
        var device = new DeviceInfo(
            Name: "my-host",
            ConfigAddress: "my-host",
            ResolvedIp: "10.0.0.5",
            Port: 161,
            PollGroups: Array.Empty<MetricPollInfo>(),
            CommunityString: "Simetra.my-host");

        var devReg = Substitute.For<IDeviceRegistry>();
        devReg.AllDevices.Returns(new[] { device });
        devReg.TryGetByIpPort(Arg.Any<string>(), Arg.Any<int>(), out Arg.Any<DeviceInfo?>())
            .Returns(true);

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
            Wrap(tenant), CreatePassthroughOidMapService(), devReg, NullLogger.Instance);

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
            NullLogger.Instance);

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
            NullLogger.Instance);

        Assert.Single(result.Tenants);
        Assert.Equal(2, result.Tenants[0].Metrics.Count);   // 2 valid metrics
        Assert.Single(result.Tenants[0].Commands);           // 1 valid command
    }
}
