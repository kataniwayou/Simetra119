using Lextm.SharpSnmpLib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

public sealed class TenantVectorRegistryTests
{
    // ──────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────

    private static TenantVectorRegistry CreateRegistry()
        => new(
            Substitute.For<IDeviceRegistry>(),
            Substitute.For<IOidMapService>(),
            NullLogger<TenantVectorRegistry>.Instance);

    /// <summary>
    /// Build a TenantVectorOptions from a flat tuple list.
    /// Each tuple: (tenantId, priority, ip, port, metricName)
    /// </summary>
    private static TenantVectorOptions CreateOptions(
        params (string tenantId, int priority, string ip, int port, string metricName)[] metrics)
    {
        var tenantMap = new Dictionary<string, (int priority, List<MetricSlotOptions> slots)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (tenantId, priority, ip, port, metricName) in metrics)
        {
            if (!tenantMap.TryGetValue(tenantId, out var entry))
            {
                entry = (priority, new List<MetricSlotOptions>());
                tenantMap[tenantId] = entry;
            }
            entry.slots.Add(new MetricSlotOptions
            {
                Ip = ip,
                Port = port,
                MetricName = metricName
            });
        }

        return new TenantVectorOptions
        {
            Tenants = tenantMap.Select(kvp => new TenantOptions
            {
                Id = kvp.Key,
                Priority = kvp.Value.priority,
                Metrics = kvp.Value.slots
            }).ToList()
        };
    }

    // ──────────────────────────────────────────────────────
    // 1. Empty state (3 tests)
    // ──────────────────────────────────────────────────────

    [Fact]
    public void NewRegistry_Groups_IsEmpty()
    {
        var registry = CreateRegistry();
        Assert.NotNull(registry.Groups);
        Assert.Empty(registry.Groups);
    }

    [Fact]
    public void NewRegistry_TryRoute_ReturnsFalse()
    {
        var registry = CreateRegistry();
        var found = registry.TryRoute("10.0.0.1", 161, "hrProcessorLoad", out var holders);
        Assert.False(found);
        Assert.Null(holders);
    }

    [Fact]
    public void NewRegistry_Counts_AreZero()
    {
        var registry = CreateRegistry();
        Assert.Equal(0, registry.TenantCount);
        Assert.Equal(0, registry.SlotCount);
    }

    // ──────────────────────────────────────────────────────
    // 2. Single tenant reload (4 tests)
    // ──────────────────────────────────────────────────────

    [Fact]
    public void Reload_SingleTenant_GroupsContainOneTenantWithCorrectSlots()
    {
        var registry = CreateRegistry();
        var options = CreateOptions(
            ("tenant-a", 1, "10.0.0.1", 161, "hrProcessorLoad"),
            ("tenant-a", 1, "10.0.0.1", 161, "ifInOctets"));

        registry.Reload(options);

        Assert.Single(registry.Groups);
        var group = registry.Groups[0];
        Assert.Equal(1, group.Priority);
        Assert.Single(group.Tenants);

        var tenant = group.Tenants[0];
        Assert.Equal("tenant-a", tenant.Id);
        Assert.Equal(2, tenant.Holders.Count);

        var holder0 = tenant.Holders[0];
        Assert.Equal("10.0.0.1", holder0.Ip);
        Assert.Equal(161, holder0.Port);
        Assert.Equal("hrProcessorLoad", holder0.MetricName);

        var holder1 = tenant.Holders[1];
        Assert.Equal("ifInOctets", holder1.MetricName);
    }

    [Fact]
    public void Reload_SingleTenant_TryRouteFindsEachMetric()
    {
        var registry = CreateRegistry();
        var options = CreateOptions(
            ("tenant-a", 1, "10.0.0.1", 161, "hrProcessorLoad"),
            ("tenant-a", 1, "10.0.0.1", 161, "ifInOctets"));

        registry.Reload(options);

        Assert.True(registry.TryRoute("10.0.0.1", 161, "hrProcessorLoad", out var h1));
        Assert.NotNull(h1);
        Assert.Single(h1);

        Assert.True(registry.TryRoute("10.0.0.1", 161, "ifInOctets", out var h2));
        Assert.NotNull(h2);
        Assert.Single(h2);
    }

    [Fact]
    public void Reload_SingleTenant_CountsAreCorrect()
    {
        var registry = CreateRegistry();
        var options = CreateOptions(
            ("tenant-a", 1, "10.0.0.1", 161, "hrProcessorLoad"),
            ("tenant-a", 1, "10.0.0.1", 161, "ifInOctets"));

        registry.Reload(options);

        Assert.Equal(1, registry.TenantCount);
        Assert.Equal(2, registry.SlotCount);
    }

    [Fact]
    public void Reload_TryRoute_CaseInsensitive()
    {
        var registry = CreateRegistry();
        var options = CreateOptions(
            ("tenant-a", 1, "10.0.0.1", 161, "hrProcessorLoad"));

        registry.Reload(options);

        Assert.True(registry.TryRoute("10.0.0.1", 161, "HRPROCESSORLOAD", out _));
        Assert.True(registry.TryRoute("10.0.0.1", 161, "HrProcessorLoad", out _));
        Assert.True(registry.TryRoute("10.0.0.1", 161, "hrprocessorload", out _));
    }

    // ──────────────────────────────────────────────────────
    // 3. Priority ordering (2 tests)
    // ──────────────────────────────────────────────────────

    [Fact]
    public void Reload_MultiplePriorities_GroupsSortedAscending()
    {
        var registry = CreateRegistry();
        var options = CreateOptions(
            ("tenant-high", 10, "10.0.0.1", 161, "hrProcessorLoad"),
            ("tenant-low",   1, "10.0.0.2", 161, "hrProcessorLoad"),
            ("tenant-mid",   5, "10.0.0.3", 161, "hrProcessorLoad"));

        registry.Reload(options);

        Assert.Equal(3, registry.Groups.Count);
        Assert.Equal(1,  registry.Groups[0].Priority);
        Assert.Equal(5,  registry.Groups[1].Priority);
        Assert.Equal(10, registry.Groups[2].Priority);
    }

    [Fact]
    public void Reload_SamePriority_TenantsInSameGroup()
    {
        var registry = CreateRegistry();
        var options = CreateOptions(
            ("tenant-a", 5, "10.0.0.1", 161, "hrProcessorLoad"),
            ("tenant-b", 5, "10.0.0.2", 161, "hrProcessorLoad"));

        registry.Reload(options);

        Assert.Single(registry.Groups);
        Assert.Equal(5, registry.Groups[0].Priority);
        Assert.Equal(2, registry.Groups[0].Tenants.Count);
    }

    // ──────────────────────────────────────────────────────
    // 4. Routing fan-out (1 test)
    // ──────────────────────────────────────────────────────

    [Fact]
    public void Reload_OverlappingMetrics_TryRouteReturnsMultipleHolders()
    {
        var registry = CreateRegistry();
        var options = CreateOptions(
            ("tenant-a", 1, "10.0.0.1", 161, "hrProcessorLoad"),
            ("tenant-b", 2, "10.0.0.1", 161, "hrProcessorLoad"));

        registry.Reload(options);

        Assert.True(registry.TryRoute("10.0.0.1", 161, "hrProcessorLoad", out var holders));
        Assert.NotNull(holders);
        Assert.Equal(2, holders.Count);
    }

    // ──────────────────────────────────────────────────────
    // 5. Value carry-over (3 tests)
    // ──────────────────────────────────────────────────────

    [Fact]
    public void Reload_CarriesOverExistingValues()
    {
        var registry = CreateRegistry();
        var options = CreateOptions(
            ("tenant-a", 1, "10.0.0.1", 161, "hrProcessorLoad"));

        registry.Reload(options);

        // Write a value into the initial holder.
        registry.TryRoute("10.0.0.1", 161, "hrProcessorLoad", out var holders);
        holders[0].WriteValue(42.0, "42", SnmpType.Integer32);

        // Reload with the same config — value should carry over.
        registry.Reload(options);

        registry.TryRoute("10.0.0.1", 161, "hrProcessorLoad", out var newHolders);
        Assert.NotNull(newHolders);
        var slot = newHolders[0].ReadSlot();
        Assert.NotNull(slot);
        Assert.Equal(42.0, slot.Value);
        Assert.Equal("42", slot.StringValue);
    }

    [Fact]
    public void Reload_CarriesOverTypeCode()
    {
        var registry = CreateRegistry();
        var options = CreateOptions(
            ("tenant-a", 1, "10.0.0.1", 161, "hrProcessorLoad"));

        registry.Reload(options);

        // Write with a specific TypeCode.
        registry.TryRoute("10.0.0.1", 161, "hrProcessorLoad", out var holders);
        holders[0].WriteValue(77.0, null, SnmpType.Gauge32);

        // Reload with the same config — TypeCode should carry over.
        registry.Reload(options);

        registry.TryRoute("10.0.0.1", 161, "hrProcessorLoad", out var newHolders);
        Assert.NotNull(newHolders);
        var slot = newHolders[0].ReadSlot();
        Assert.NotNull(slot);
        Assert.Equal(SnmpType.Gauge32, slot.TypeCode);
    }

    [Fact]
    public void Reload_NewMetric_StartsWithNullSlot()
    {
        var registry = CreateRegistry();

        // Initial config: one metric.
        var options1 = CreateOptions(
            ("tenant-a", 1, "10.0.0.1", 161, "hrProcessorLoad"));
        registry.Reload(options1);

        // Second reload adds a new metric.
        var options2 = CreateOptions(
            ("tenant-a", 1, "10.0.0.1", 161, "hrProcessorLoad"),
            ("tenant-a", 1, "10.0.0.1", 161, "ifInOctets"));
        registry.Reload(options2);

        registry.TryRoute("10.0.0.1", 161, "ifInOctets", out var holders);
        Assert.NotNull(holders);
        Assert.Null(holders[0].ReadSlot());
    }

    [Fact]
    public void Reload_RemovedMetric_ValueIsLost()
    {
        var registry = CreateRegistry();

        // Initial config with two metrics.
        var options1 = CreateOptions(
            ("tenant-a", 1, "10.0.0.1", 161, "hrProcessorLoad"),
            ("tenant-a", 1, "10.0.0.1", 161, "ifInOctets"));
        registry.Reload(options1);

        // Write a value to ifInOctets.
        registry.TryRoute("10.0.0.1", 161, "ifInOctets", out var holders);
        holders[0].WriteValue(99.0, null, SnmpType.Integer32);

        // Reload without ifInOctets — it should be gone.
        var options2 = CreateOptions(
            ("tenant-a", 1, "10.0.0.1", 161, "hrProcessorLoad"));
        registry.Reload(options2);

        var found = registry.TryRoute("10.0.0.1", 161, "ifInOctets", out _);
        Assert.False(found);
    }

    // ──────────────────────────────────────────────────────
    // 6. Rebuild atomicity (1 test)
    // ──────────────────────────────────────────────────────

    [Fact]
    public void Reload_ReplacesEntireIndexAtomically()
    {
        var registry = CreateRegistry();

        // First config: tenant-a with metric-A.
        var options1 = CreateOptions(
            ("tenant-a", 1, "10.0.0.1", 161, "metricA"));
        registry.Reload(options1);

        Assert.True(registry.TryRoute("10.0.0.1", 161, "metricA", out _));

        // Second config: tenant-b with metric-B (completely different).
        var options2 = CreateOptions(
            ("tenant-b", 2, "192.168.1.1", 161, "metricB"));
        registry.Reload(options2);

        // Old key gone, new key present.
        Assert.False(registry.TryRoute("10.0.0.1", 161, "metricA", out _));
        Assert.True(registry.TryRoute("192.168.1.1", 161, "metricB", out _));
    }

    // ──────────────────────────────────────────────────────
    // 7. Diff logging (1 test)
    // ──────────────────────────────────────────────────────

    [Fact]
    public void Reload_LogsDiffInformation()
    {
        var logger = new CapturingLogger();
        var registry = new TenantVectorRegistry(
            Substitute.For<IDeviceRegistry>(),
            Substitute.For<IOidMapService>(),
            logger);

        var options1 = CreateOptions(
            ("tenant-a", 1, "10.0.0.1", 161, "hrProcessorLoad"));
        registry.Reload(options1);

        var options2 = CreateOptions(
            ("tenant-a", 1, "10.0.0.1", 161, "hrProcessorLoad"),
            ("tenant-b", 2, "10.0.0.2", 161, "ifInOctets"));
        registry.Reload(options2);

        Assert.True(logger.LogMessages.Count >= 2, "Expected at least 2 log messages from two reloads");
        // Both log messages should contain "reloaded" and slot/tenant count info.
        Assert.All(logger.LogMessages, msg => Assert.Contains("reloaded", msg, StringComparison.OrdinalIgnoreCase));
        Assert.All(logger.LogMessages, msg => Assert.Contains("tenants=", msg, StringComparison.OrdinalIgnoreCase));
        Assert.All(logger.LogMessages, msg => Assert.Contains("slots=", msg, StringComparison.OrdinalIgnoreCase));
    }

    // ──────────────────────────────────────────────────────
    // Test logger helper
    // ──────────────────────────────────────────────────────

    private sealed class CapturingLogger : ILogger<TenantVectorRegistry>
    {
        public List<string> LogMessages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Information)
                LogMessages.Add(formatter(state, exception));
        }
    }
}
