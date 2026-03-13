using Microsoft.Extensions.Logging.Abstractions;
using SnmpCollector.Services;
using Xunit;

namespace SnmpCollector.Tests.Services;

public class OidMapWatcherValidationTests
{
    private static readonly NullLogger<OidMapWatcherService> Logger =
        NullLogger<OidMapWatcherService>.Instance;

    [Fact]
    public void ValidOidMap_NoDuplicates_ReturnsAllEntries()
    {
        var json = """
            {
                "1.3.6.1.2.1.1.1.0": "sysDescr",
                "1.3.6.1.2.1.1.3.0": "sysUpTime",
                "1.3.6.1.2.1.1.5.0": "sysName"
            }
            """;

        var result = OidMapWatcherService.ValidateAndParseOidMap(json, Logger);

        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal("sysDescr", result["1.3.6.1.2.1.1.1.0"]);
        Assert.Equal("sysUpTime", result["1.3.6.1.2.1.1.3.0"]);
        Assert.Equal("sysName", result["1.3.6.1.2.1.1.5.0"]);
    }

    [Fact]
    public void DuplicateOidKey_BothEntriesSkipped()
    {
        // Same OID key appears twice with different metric names
        var json = """
            {
                "1.3.6.1.2.1.1.1.0": "sysDescr",
                "1.3.6.1.2.1.1.1.0": "sysDescrAlt"
            }
            """;

        var result = OidMapWatcherService.ValidateAndParseOidMap(json, Logger);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void DuplicateMetricName_BothEntriesSkipped()
    {
        // Two different OIDs map to the same metric name
        var json = """
            {
                "1.3.6.1.2.1.1.1.0": "sysDescr",
                "1.3.6.1.2.1.1.5.0": "sysDescr"
            }
            """;

        var result = OidMapWatcherService.ValidateAndParseOidMap(json, Logger);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void DuplicateOid_OtherEntriesSurvive()
    {
        var json = """
            {
                "1.3.6.1.2.1.1.1.0": "sysDescr",
                "1.3.6.1.2.1.1.1.0": "sysDescrAlt",
                "1.3.6.1.2.1.1.5.0": "sysName"
            }
            """;

        var result = OidMapWatcherService.ValidateAndParseOidMap(json, Logger);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("sysName", result["1.3.6.1.2.1.1.5.0"]);
    }

    [Fact]
    public void DuplicateMetricName_OtherEntriesSurvive()
    {
        var json = """
            {
                "1.3.6.1.2.1.1.1.0": "sysDescr",
                "1.3.6.1.2.1.1.3.0": "sysDescr",
                "1.3.6.1.2.1.1.5.0": "sysName"
            }
            """;

        var result = OidMapWatcherService.ValidateAndParseOidMap(json, Logger);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("sysName", result["1.3.6.1.2.1.1.5.0"]);
    }

    [Fact]
    public void AllDuplicates_ReturnsEmptyDictionary()
    {
        // Both OID duplicates and name duplicates -- everything conflicts
        var json = """
            {
                "1.3.6.1.2.1.1.1.0": "sysDescr",
                "1.3.6.1.2.1.1.1.0": "sysDescrAlt",
                "1.3.6.1.2.1.1.3.0": "sysUpTime",
                "1.3.6.1.2.1.1.5.0": "sysUpTime"
            }
            """;

        var result = OidMapWatcherService.ValidateAndParseOidMap(json, Logger);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void EmptyJsonObject_ReturnsEmptyDictionary()
    {
        var json = "{}";

        var result = OidMapWatcherService.ValidateAndParseOidMap(json, Logger);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void NullOrEmptyMetricName_EntrySkipped()
    {
        var json = """
            {
                "1.3.6.1.2.1.1.1.0": "",
                "1.3.6.1.2.1.1.3.0": null,
                "1.3.6.1.2.1.1.5.0": "sysName"
            }
            """;

        var result = OidMapWatcherService.ValidateAndParseOidMap(json, Logger);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("sysName", result["1.3.6.1.2.1.1.5.0"]);
    }

    [Fact]
    public void JsonWithComments_ParsesSuccessfully()
    {
        var json = """
            {
                // System description OID
                "1.3.6.1.2.1.1.1.0": "sysDescr",
                // System uptime OID
                "1.3.6.1.2.1.1.3.0": "sysUpTime"
            }
            """;

        var result = OidMapWatcherService.ValidateAndParseOidMap(json, Logger);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("sysDescr", result["1.3.6.1.2.1.1.1.0"]);
        Assert.Equal("sysUpTime", result["1.3.6.1.2.1.1.3.0"]);
    }
}
