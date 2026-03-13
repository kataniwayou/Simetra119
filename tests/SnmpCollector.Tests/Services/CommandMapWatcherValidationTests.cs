using Microsoft.Extensions.Logging.Abstractions;
using SnmpCollector.Services;
using Xunit;

namespace SnmpCollector.Tests.Services;

public class CommandMapWatcherValidationTests
{
    private static readonly NullLogger<CommandMapWatcherService> Logger =
        NullLogger<CommandMapWatcherService>.Instance;

    [Fact]
    public void ValidCommandMap_NoDuplicates_ReturnsAllEntries()
    {
        var json = """
            [
                { "Oid": "1.3.6.1.4.1.9.9.1.0", "CommandName": "obp_set_bypass_L1" },
                { "Oid": "1.3.6.1.4.1.9.9.2.0", "CommandName": "obp_set_bypass_L2" },
                { "Oid": "1.3.6.1.4.1.9.9.3.0", "CommandName": "obp_set_bypass_L3" }
            ]
            """;

        var result = CommandMapWatcherService.ValidateAndParseCommandMap(json, Logger);

        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal("obp_set_bypass_L1", result["1.3.6.1.4.1.9.9.1.0"]);
        Assert.Equal("obp_set_bypass_L2", result["1.3.6.1.4.1.9.9.2.0"]);
        Assert.Equal("obp_set_bypass_L3", result["1.3.6.1.4.1.9.9.3.0"]);
    }

    [Fact]
    public void DuplicateOidKey_BothEntriesSkipped()
    {
        // Same OID appears twice with different command names
        var json = """
            [
                { "Oid": "1.3.6.1.4.1.9.9.1.0", "CommandName": "obp_set_bypass_L1" },
                { "Oid": "1.3.6.1.4.1.9.9.1.0", "CommandName": "obp_set_bypass_L1_alt" }
            ]
            """;

        var result = CommandMapWatcherService.ValidateAndParseCommandMap(json, Logger);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void DuplicateCommandName_BothEntriesSkipped()
    {
        // Two different OIDs map to the same command name
        var json = """
            [
                { "Oid": "1.3.6.1.4.1.9.9.1.0", "CommandName": "obp_set_bypass_L1" },
                { "Oid": "1.3.6.1.4.1.9.9.5.0", "CommandName": "obp_set_bypass_L1" }
            ]
            """;

        var result = CommandMapWatcherService.ValidateAndParseCommandMap(json, Logger);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void DuplicateOid_OtherEntriesSurvive()
    {
        var json = """
            [
                { "Oid": "1.3.6.1.4.1.9.9.1.0", "CommandName": "obp_set_bypass_L1" },
                { "Oid": "1.3.6.1.4.1.9.9.1.0", "CommandName": "obp_set_bypass_L1_alt" },
                { "Oid": "1.3.6.1.4.1.9.9.3.0", "CommandName": "obp_set_bypass_L3" }
            ]
            """;

        var result = CommandMapWatcherService.ValidateAndParseCommandMap(json, Logger);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("obp_set_bypass_L3", result["1.3.6.1.4.1.9.9.3.0"]);
    }

    [Fact]
    public void DuplicateCommandName_OtherEntriesSurvive()
    {
        var json = """
            [
                { "Oid": "1.3.6.1.4.1.9.9.1.0", "CommandName": "obp_set_bypass_L1" },
                { "Oid": "1.3.6.1.4.1.9.9.2.0", "CommandName": "obp_set_bypass_L1" },
                { "Oid": "1.3.6.1.4.1.9.9.3.0", "CommandName": "obp_set_bypass_L3" }
            ]
            """;

        var result = CommandMapWatcherService.ValidateAndParseCommandMap(json, Logger);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("obp_set_bypass_L3", result["1.3.6.1.4.1.9.9.3.0"]);
    }

    [Fact]
    public void AllDuplicates_ReturnsEmptyDictionary()
    {
        // Both OID duplicates and command name duplicates -- everything conflicts
        var json = """
            [
                { "Oid": "1.3.6.1.4.1.9.9.1.0", "CommandName": "obp_set_bypass_L1" },
                { "Oid": "1.3.6.1.4.1.9.9.1.0", "CommandName": "obp_set_bypass_L1_alt" },
                { "Oid": "1.3.6.1.4.1.9.9.3.0", "CommandName": "obp_set_bypass_L3" },
                { "Oid": "1.3.6.1.4.1.9.9.5.0", "CommandName": "obp_set_bypass_L3" }
            ]
            """;

        var result = CommandMapWatcherService.ValidateAndParseCommandMap(json, Logger);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void EmptyJsonArray_ReturnsEmptyDictionary()
    {
        var json = "[]";

        var result = CommandMapWatcherService.ValidateAndParseCommandMap(json, Logger);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void NullOrEmptyCommandName_EntrySkipped()
    {
        var json = """
            [
                { "Oid": "1.3.6.1.4.1.9.9.1.0", "CommandName": "" },
                { "Oid": "1.3.6.1.4.1.9.9.2.0", "CommandName": null },
                { "Oid": "1.3.6.1.4.1.9.9.3.0", "CommandName": "obp_set_bypass_L3" }
            ]
            """;

        var result = CommandMapWatcherService.ValidateAndParseCommandMap(json, Logger);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("obp_set_bypass_L3", result["1.3.6.1.4.1.9.9.3.0"]);
    }

    [Fact]
    public void JsonWithComments_ParsesSuccessfully()
    {
        var json = """
            [
                // Bypass L1 command OID
                { "Oid": "1.3.6.1.4.1.9.9.1.0", "CommandName": "obp_set_bypass_L1" },
                // Bypass L2 command OID
                { "Oid": "1.3.6.1.4.1.9.9.2.0", "CommandName": "obp_set_bypass_L2" }
            ]
            """;

        var result = CommandMapWatcherService.ValidateAndParseCommandMap(json, Logger);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("obp_set_bypass_L1", result["1.3.6.1.4.1.9.9.1.0"]);
        Assert.Equal("obp_set_bypass_L2", result["1.3.6.1.4.1.9.9.2.0"]);
    }

    [Fact]
    public void NonArrayJson_ReturnsNull()
    {
        // Not an array -- should hit the ValueKind.Array check and return null
        var json = """{ "not": "an array" }""";

        var result = CommandMapWatcherService.ValidateAndParseCommandMap(json, Logger);

        Assert.Null(result);
    }
}
