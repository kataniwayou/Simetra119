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
            [
                { "Oid": "1.3.6.1.2.1.1.1.0", "MetricName": "sysDescr" },
                { "Oid": "1.3.6.1.2.1.1.3.0", "MetricName": "sysUpTime" },
                { "Oid": "1.3.6.1.2.1.1.5.0", "MetricName": "sysName" }
            ]
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
        // Same OID appears twice with different metric names
        var json = """
            [
                { "Oid": "1.3.6.1.2.1.1.1.0", "MetricName": "sysDescr" },
                { "Oid": "1.3.6.1.2.1.1.1.0", "MetricName": "sysDescrAlt" }
            ]
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
            [
                { "Oid": "1.3.6.1.2.1.1.1.0", "MetricName": "sysDescr" },
                { "Oid": "1.3.6.1.2.1.1.5.0", "MetricName": "sysDescr" }
            ]
            """;

        var result = OidMapWatcherService.ValidateAndParseOidMap(json, Logger);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void DuplicateOid_OtherEntriesSurvive()
    {
        var json = """
            [
                { "Oid": "1.3.6.1.2.1.1.1.0", "MetricName": "sysDescr" },
                { "Oid": "1.3.6.1.2.1.1.1.0", "MetricName": "sysDescrAlt" },
                { "Oid": "1.3.6.1.2.1.1.5.0", "MetricName": "sysName" }
            ]
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
            [
                { "Oid": "1.3.6.1.2.1.1.1.0", "MetricName": "sysDescr" },
                { "Oid": "1.3.6.1.2.1.1.3.0", "MetricName": "sysDescr" },
                { "Oid": "1.3.6.1.2.1.1.5.0", "MetricName": "sysName" }
            ]
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
            [
                { "Oid": "1.3.6.1.2.1.1.1.0", "MetricName": "sysDescr" },
                { "Oid": "1.3.6.1.2.1.1.1.0", "MetricName": "sysDescrAlt" },
                { "Oid": "1.3.6.1.2.1.1.3.0", "MetricName": "sysUpTime" },
                { "Oid": "1.3.6.1.2.1.1.5.0", "MetricName": "sysUpTime" }
            ]
            """;

        var result = OidMapWatcherService.ValidateAndParseOidMap(json, Logger);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void EmptyJsonArray_ReturnsEmptyDictionary()
    {
        var json = "[]";

        var result = OidMapWatcherService.ValidateAndParseOidMap(json, Logger);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void NullOrEmptyMetricName_EntrySkipped()
    {
        var json = """
            [
                { "Oid": "1.3.6.1.2.1.1.1.0", "MetricName": "" },
                { "Oid": "1.3.6.1.2.1.1.3.0", "MetricName": null },
                { "Oid": "1.3.6.1.2.1.1.5.0", "MetricName": "sysName" }
            ]
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
            [
                // System description OID
                { "Oid": "1.3.6.1.2.1.1.1.0", "MetricName": "sysDescr" },
                // System uptime OID
                { "Oid": "1.3.6.1.2.1.1.3.0", "MetricName": "sysUpTime" }
            ]
            """;

        var result = OidMapWatcherService.ValidateAndParseOidMap(json, Logger);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("sysDescr", result["1.3.6.1.2.1.1.1.0"]);
        Assert.Equal("sysUpTime", result["1.3.6.1.2.1.1.3.0"]);
    }

    [Fact]
    public void NonArrayJson_ReturnsNull()
    {
        // Not an array -- should hit the ValueKind.Array check and return null
        var json = """{ "not": "an array" }""";

        var result = OidMapWatcherService.ValidateAndParseOidMap(json, Logger);

        Assert.Null(result);
    }
}
