using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SnmpCollector.Configuration;
using System.Text.RegularExpressions;
using Xunit;

namespace SnmpCollector.Tests.Configuration;

/// <summary>
/// Integration tests verifying OID map configuration: JSONC parsing, multi-file merge,
/// OBP entry count, naming convention, and OID prefix consistency.
/// Tests read from the standalone oidmaps.json file.
/// </summary>
public class OidMapAutoScanTests
{
    /// <summary>
    /// Locates the real oidmaps.json relative to the test assembly output directory.
    /// Path: {testBin}/../../../../src/SnmpCollector/config/oidmaps.json
    /// </summary>
    private static string GetOidMapsPath()
    {
        var testDir = Path.GetDirectoryName(typeof(OidMapAutoScanTests).Assembly.Location)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "src", "SnmpCollector", "config", "oidmaps.json");
    }

    /// <summary>
    /// Parses oidmaps.json (JSONC) and returns the OID map dictionary.
    /// Matches the production parsing pattern in Program.cs and OidMapWatcherService.
    /// </summary>
    private static Dictionary<string, string> LoadOidMap()
    {
        var path = GetOidMapsPath();
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true
        };
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, options)!;
    }

    /// <summary>
    /// Binds the OidMap section from an IConfiguration into OidMapOptions.Entries,
    /// matching the legacy binding pattern for IConfiguration-based tests.
    /// </summary>
    private static OidMapOptions BindOidMapOptions(IConfiguration config)
    {
        var options = new OidMapOptions();
        config.GetSection(OidMapOptions.SectionName).Bind(options.Entries);
        return options;
    }

    [Fact]
    public void LoadsOidMapFromJsoncFile()
    {
        // Arrange: create a temp JSONC file with // comments
        var tempFile = Path.Combine(Path.GetTempPath(), $"oidmap-test-{Guid.NewGuid()}.json");
        try
        {
            var jsonc = """
                {
                  // This is a JSONC comment -- must not cause parse errors
                  "OidMap": {
                    // CPU load OID
                    "1.3.6.1.2.1.25.3.3.1.2": "hrProcessorLoad",
                    // Uptime OID
                    "1.3.6.1.2.1.1.3.0": "sysUpTime"
                  }
                }
                """;
            File.WriteAllText(tempFile, jsonc);

            // Act
            var config = new ConfigurationBuilder()
                .AddJsonFile(tempFile, optional: false, reloadOnChange: false)
                .Build();

            var options = BindOidMapOptions(config);

            // Assert: comments did not cause errors and entries are present
            Assert.Equal(2, options.Entries.Count);
            Assert.Equal("hrProcessorLoad", options.Entries["1.3.6.1.2.1.25.3.3.1.2"]);
            Assert.Equal("sysUpTime", options.Entries["1.3.6.1.2.1.1.3.0"]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void MergesMultipleOidMapFiles()
    {
        // Arrange: two separate JSONC files with different OID entries under "OidMap"
        var tempFile1 = Path.Combine(Path.GetTempPath(), $"oidmap-merge1-{Guid.NewGuid()}.json");
        var tempFile2 = Path.Combine(Path.GetTempPath(), $"oidmap-merge2-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(tempFile1, """
                {
                  "OidMap": {
                    "1.3.6.1.2.1.25.3.3.1.2": "hrProcessorLoad"
                  }
                }
                """);

            File.WriteAllText(tempFile2, """
                {
                  "OidMap": {
                    "1.3.6.1.2.1.1.3.0": "sysUpTime",
                    "1.3.6.1.2.1.2.2.1.10": "ifInOctets"
                  }
                }
                """);

            // Act: load both files (simulating auto-scan merge)
            var config = new ConfigurationBuilder()
                .AddJsonFile(tempFile1, optional: false, reloadOnChange: false)
                .AddJsonFile(tempFile2, optional: false, reloadOnChange: false)
                .Build();

            var options = BindOidMapOptions(config);

            // Assert: entries from BOTH files are merged
            Assert.Equal(3, options.Entries.Count);
            Assert.True(options.Entries.ContainsKey("1.3.6.1.2.1.25.3.3.1.2"), "File 1 entry missing");
            Assert.True(options.Entries.ContainsKey("1.3.6.1.2.1.1.3.0"), "File 2 entry 1 missing");
            Assert.True(options.Entries.ContainsKey("1.3.6.1.2.1.2.2.1.10"), "File 2 entry 2 missing");
        }
        finally
        {
            File.Delete(tempFile1);
            File.Delete(tempFile2);
        }
    }

    [Fact]
    public void ObpOidMapHas24Entries()
    {
        // Arrange: load OBP entries from oidmaps.json
        var path = GetOidMapsPath();
        Assert.True(File.Exists(path), $"oidmaps.json not found at: {path}");

        var oidMap = LoadOidMap();

        // Filter to OBP entries only (enterprise prefix 1.3.6.1.4.1.47477.10.21)
        var obpEntries = oidMap
            .Where(kv => kv.Key.StartsWith("1.3.6.1.4.1.47477.10.21."))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Assert: exactly 24 entries (4 links x 6 metrics)
        Assert.Equal(24, obpEntries.Count);

        // Spot-check specific entries across different links and metric types
        Assert.Equal("obp_link_state_L1", obpEntries["1.3.6.1.4.1.47477.10.21.1.3.1.0"]);
        Assert.Equal("obp_r4_power_L4", obpEntries["1.3.6.1.4.1.47477.10.21.4.3.13.0"]);
        Assert.Equal("obp_channel_L2", obpEntries["1.3.6.1.4.1.47477.10.21.2.3.4.0"]);
    }

    [Fact]
    public void ObpOidNamingConventionIsConsistent()
    {
        // Arrange: load OBP entries from oidmaps.json
        var oidMap = LoadOidMap();
        var obpEntries = oidMap
            .Where(kv => kv.Key.StartsWith("1.3.6.1.4.1.47477.10.21."))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Act & Assert: all OBP metric names match obp_{metric}_L{1-4} pattern
        var pattern = new Regex(@"^obp_(link_state|channel|r[1-4]_power)_L[1-4]$");

        foreach (var (oid, metricName) in obpEntries)
        {
            Assert.Matches(pattern, metricName);
        }
    }

    [Fact]
    public void ObpOidStringsFollowEnterprisePrefix()
    {
        // Arrange: load OBP entries from oidmaps.json
        var oidMap = LoadOidMap();
        var obpEntries = oidMap
            .Where(kv => kv.Key.StartsWith("1.3.6.1.4.1.47477.10.21."))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Act & Assert: all OID keys start with enterprise prefix and end with .0
        const string enterprisePrefix = "1.3.6.1.4.1.47477.10.21.";

        foreach (var oid in obpEntries.Keys)
        {
            Assert.StartsWith(enterprisePrefix, oid);
            Assert.EndsWith(".0", oid);
        }
    }
}
