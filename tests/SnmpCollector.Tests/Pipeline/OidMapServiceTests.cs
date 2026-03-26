using Microsoft.Extensions.Logging.Abstractions;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

public sealed class OidMapServiceTests
{
    private static OidMapService CreateService(Dictionary<string, string> entries)
    {
        return new OidMapService(entries, NullLogger<OidMapService>.Instance);
    }

    [Fact]
    public void Resolve_KnownOid_ReturnsMetricName()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.25.3.3.1.2"] = "hrProcessorLoad"
        });

        var result = sut.Resolve("1.3.6.1.2.1.25.3.3.1.2");

        Assert.Equal("hrProcessorLoad", result);
    }

    [Fact]
    public void Resolve_UnknownOid_ReturnsUnknown()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.25.3.3.1.2"] = "hrProcessorLoad"
        });

        var result = sut.Resolve("1.3.6.1.999.999");

        Assert.Equal(OidMapService.Unknown, result);
    }

    [Fact]
    public void Resolve_EmptyMap_AlwaysReturnsUnknown()
    {
        var sut = CreateService(new Dictionary<string, string>());

        var result = sut.Resolve("1.3.6.1.2.1.1.1.0");

        Assert.Equal(OidMapService.Unknown, result);
    }

    [Fact]
    public void EntryCount_MatchesDictionarySize()
    {
        // OidMapService always seeds the heartbeat OID at construction time,
        // so EntryCount = supplied entries + 1 (heartbeat seed).
        var entries = new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.1.1.0"] = "sysDescr",
            ["1.3.6.1.2.1.1.3.0"] = "sysUpTime",
            ["1.3.6.1.2.1.25.3.3.1.2"] = "hrProcessorLoad"
        };
        var sut = CreateService(entries);

        Assert.Equal(4, sut.EntryCount); // 3 supplied + 1 heartbeat seed
    }

    [Fact]
    public void Resolve_AfterReload_NewOidResolves()
    {
        var initialEntries = new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.1.1.0"] = "sysDescr"
        };
        var sut = CreateService(initialEntries);

        // Simulate hot-reload adding a new entry via UpdateMap
        var updatedEntries = new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.1.1.0"] = "sysDescr",
            ["1.3.6.1.2.1.25.3.3.1.2"] = "hrProcessorLoad"
        };
        sut.UpdateMap(updatedEntries);

        var result = sut.Resolve("1.3.6.1.2.1.25.3.3.1.2");

        Assert.Equal("hrProcessorLoad", result);
    }

    [Fact]
    public void Resolve_AfterReload_RemovedOidReturnsUnknown()
    {
        var initialEntries = new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.1.1.0"] = "sysDescr",
            ["1.3.6.1.2.1.25.3.3.1.2"] = "hrProcessorLoad"
        };
        var sut = CreateService(initialEntries);

        // Simulate hot-reload removing an entry via UpdateMap
        var updatedEntries = new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.1.1.0"] = "sysDescr"
        };
        sut.UpdateMap(updatedEntries);

        var result = sut.Resolve("1.3.6.1.2.1.25.3.3.1.2");

        Assert.Equal(OidMapService.Unknown, result);
    }

    [Fact]
    public void ResolveToOid_KnownName_ReturnsOid()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.25.3.3.1.2"] = "hrProcessorLoad"
        });

        var result = sut.ResolveToOid("hrProcessorLoad");

        Assert.Equal("1.3.6.1.2.1.25.3.3.1.2", result);
    }

    [Fact]
    public void ResolveToOid_UnknownName_ReturnsNull()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.25.3.3.1.2"] = "hrProcessorLoad"
        });

        var result = sut.ResolveToOid("no_such_metric");

        Assert.Null(result);
    }

    [Fact]
    public void ResolveToOid_Heartbeat_ReturnsHeartbeatOid()
    {
        var sut = CreateService(new Dictionary<string, string>());

        var result = sut.ResolveToOid("Heartbeat");

        Assert.Equal(SnmpHeartbeatJobOptions.HeartbeatOid, result);
    }

    [Fact]
    public void ResolveToOid_AfterReload_NewNameResolves()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.1.1.0"] = "sysDescr"
        });

        sut.UpdateMap(new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.1.1.0"] = "sysDescr",
            ["1.3.6.1.2.1.25.3.3.1.2"] = "hrProcessorLoad"
        });

        var result = sut.ResolveToOid("hrProcessorLoad");

        Assert.Equal("1.3.6.1.2.1.25.3.3.1.2", result);
    }

    [Fact]
    public void ResolveToOid_AfterReload_RemovedNameReturnsNull()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.1.1.0"] = "sysDescr",
            ["1.3.6.1.2.1.25.3.3.1.2"] = "hrProcessorLoad"
        });

        sut.UpdateMap(new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.1.1.0"] = "sysDescr"
        });

        var result = sut.ResolveToOid("hrProcessorLoad");

        Assert.Null(result);
    }
}
