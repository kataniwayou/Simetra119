using Microsoft.Extensions.Logging.Abstractions;
using SnmpCollector.Pipeline;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

public sealed class CommandMapServiceTests
{
    private static CommandMapService CreateService(Dictionary<string, string>? entries = null)
    {
        return new CommandMapService(
            entries ?? new Dictionary<string, string>(),
            NullLogger<CommandMapService>.Instance);
    }

    [Fact]
    public void ResolveCommandName_KnownOid_ReturnsName()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.4.1.47477.10.21.1.4.1.0"] = "obp_set_bypass_L1"
        });

        var result = sut.ResolveCommandName("1.3.6.1.4.1.47477.10.21.1.4.1.0");

        Assert.Equal("obp_set_bypass_L1", result);
    }

    [Fact]
    public void ResolveCommandName_UnknownOid_ReturnsNull()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.4.1.47477.10.21.1.4.1.0"] = "obp_set_bypass_L1"
        });

        var result = sut.ResolveCommandName("1.3.6.1.999.999");

        Assert.Null(result);
    }

    [Fact]
    public void ResolveCommandOid_KnownName_ReturnsOid()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.4.1.47477.10.21.1.4.1.0"] = "obp_set_bypass_L1"
        });

        var result = sut.ResolveCommandOid("obp_set_bypass_L1");

        Assert.Equal("1.3.6.1.4.1.47477.10.21.1.4.1.0", result);
    }

    [Fact]
    public void ResolveCommandOid_UnknownName_ReturnsNull()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.4.1.47477.10.21.1.4.1.0"] = "obp_set_bypass_L1"
        });

        var result = sut.ResolveCommandOid("no_such_command");

        Assert.Null(result);
    }

    [Fact]
    public void GetAllCommandNames_ReturnsAllNames()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.4.1.47477.10.21.1.4.1.0"] = "obp_set_bypass_L1",
            ["1.3.6.1.4.1.47477.10.21.2.4.1.0"] = "obp_set_bypass_L2",
            ["1.3.6.1.4.1.47477.100.3.1.1.0"]   = "npb_reset_counters_P1"
        });

        var names = sut.GetAllCommandNames();

        Assert.Equal(3, names.Count);
        Assert.Contains("obp_set_bypass_L1", names);
        Assert.Contains("obp_set_bypass_L2", names);
        Assert.Contains("npb_reset_counters_P1", names);
    }

    [Fact]
    public void Contains_KnownName_ReturnsTrue()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.4.1.47477.10.21.1.4.1.0"] = "obp_set_bypass_L1"
        });

        Assert.True(sut.Contains("obp_set_bypass_L1"));
    }

    [Fact]
    public void Contains_UnknownName_ReturnsFalse()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.4.1.47477.10.21.1.4.1.0"] = "obp_set_bypass_L1"
        });

        Assert.False(sut.Contains("no_such"));
    }

    [Fact]
    public void Count_ReturnsEntryCount()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.4.1.47477.10.21.1.4.1.0"] = "obp_set_bypass_L1",
            ["1.3.6.1.4.1.47477.10.21.2.4.1.0"] = "obp_set_bypass_L2",
            ["1.3.6.1.4.1.47477.100.3.1.1.0"]   = "npb_reset_counters_P1"
        });

        Assert.Equal(3, sut.Count);
    }

    [Fact]
    public void Count_EmptyMap_ReturnsZero()
    {
        var sut = CreateService();

        Assert.Equal(0, sut.Count);
    }

    [Fact]
    public void UpdateMap_NewEntry_BothLookupsWork()
    {
        var sut = CreateService();

        sut.UpdateMap(new Dictionary<string, string>
        {
            ["1.3.6.1.4.1.47477.10.21.1.4.1.0"] = "obp_set_bypass_L1"
        });

        Assert.Equal("obp_set_bypass_L1", sut.ResolveCommandName("1.3.6.1.4.1.47477.10.21.1.4.1.0"));
        Assert.Equal("1.3.6.1.4.1.47477.10.21.1.4.1.0", sut.ResolveCommandOid("obp_set_bypass_L1"));
    }

    [Fact]
    public void UpdateMap_RemovedEntry_ReturnsNull()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.4.1.47477.10.21.1.4.1.0"] = "obp_set_bypass_L1",
            ["1.3.6.1.4.1.47477.10.21.2.4.1.0"] = "obp_set_bypass_L2"
        });

        // Remove L2, keep L1
        sut.UpdateMap(new Dictionary<string, string>
        {
            ["1.3.6.1.4.1.47477.10.21.1.4.1.0"] = "obp_set_bypass_L1"
        });

        // Removed entry returns null from both directions
        Assert.Null(sut.ResolveCommandName("1.3.6.1.4.1.47477.10.21.2.4.1.0"));
        Assert.Null(sut.ResolveCommandOid("obp_set_bypass_L2"));

        // Surviving entry still works
        Assert.Equal("obp_set_bypass_L1", sut.ResolveCommandName("1.3.6.1.4.1.47477.10.21.1.4.1.0"));
        Assert.Equal("1.3.6.1.4.1.47477.10.21.1.4.1.0", sut.ResolveCommandOid("obp_set_bypass_L1"));
    }

    [Fact]
    public void UpdateMap_ChangedEntry_ReflectsNewName()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.1.0"] = "old_name"
        });

        sut.UpdateMap(new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.1.0"] = "new_name"
        });

        // Forward lookup returns new name
        Assert.Equal("new_name", sut.ResolveCommandName("1.3.6.1.2.1.1.0"));

        // Reverse lookup for new name returns the OID
        Assert.Equal("1.3.6.1.2.1.1.0", sut.ResolveCommandOid("new_name"));

        // Reverse lookup for old name returns null
        Assert.Null(sut.ResolveCommandOid("old_name"));
    }
}
