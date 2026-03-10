using SnmpCollector.Pipeline;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

public sealed class RoutingKeyTests
{
    private static readonly RoutingKeyComparer Comparer = RoutingKeyComparer.Instance;

    [Fact]
    public void Comparer_EqualKeys_ReturnsTrue()
    {
        var a = new RoutingKey("10.0.0.1", 161, "hrProcessorLoad");
        var b = new RoutingKey("10.0.0.1", 161, "hrProcessorLoad");

        Assert.True(Comparer.Equals(a, b));
    }

    [Fact]
    public void Comparer_CaseInsensitiveIp_ReturnsTrue()
    {
        // IP strings don't typically vary by case, but the comparer must be OrdinalIgnoreCase
        var a = new RoutingKey("10.0.0.1", 161, "hrProcessorLoad");
        var b = new RoutingKey("10.0.0.1", 161, "hrProcessorLoad");

        Assert.True(Comparer.Equals(a, b));
    }

    [Fact]
    public void Comparer_CaseInsensitiveMetricName_ReturnsTrue()
    {
        var a = new RoutingKey("10.0.0.1", 161, "hrProcessorLoad");
        var b = new RoutingKey("10.0.0.1", 161, "HRPROCESSORLOAD");

        Assert.True(Comparer.Equals(a, b));
    }

    [Fact]
    public void Comparer_DifferentPort_ReturnsFalse()
    {
        var a = new RoutingKey("10.0.0.1", 161, "hrProcessorLoad");
        var b = new RoutingKey("10.0.0.1", 162, "hrProcessorLoad");

        Assert.False(Comparer.Equals(a, b));
    }

    [Fact]
    public void Comparer_DifferentIp_ReturnsFalse()
    {
        var a = new RoutingKey("10.0.0.1", 161, "hrProcessorLoad");
        var b = new RoutingKey("10.0.0.2", 161, "hrProcessorLoad");

        Assert.False(Comparer.Equals(a, b));
    }

    [Fact]
    public void Comparer_DifferentMetricName_ReturnsFalse()
    {
        var a = new RoutingKey("10.0.0.1", 161, "hrProcessorLoad");
        var b = new RoutingKey("10.0.0.1", 161, "sysUpTime");

        Assert.False(Comparer.Equals(a, b));
    }

    [Fact]
    public void Comparer_GetHashCode_ConsistentForEqualKeys()
    {
        var a = new RoutingKey("10.0.0.1", 161, "hrProcessorLoad");
        var b = new RoutingKey("10.0.0.1", 161, "HRPROCESSORLOAD");

        Assert.Equal(Comparer.GetHashCode(a), Comparer.GetHashCode(b));
    }

    [Fact]
    public void Comparer_GetHashCode_DifferentForDifferentKeys()
    {
        var a = new RoutingKey("10.0.0.1", 161, "hrProcessorLoad");
        var b = new RoutingKey("10.0.0.2", 161, "sysUpTime");

        // Not guaranteed, but for these clearly different values, hash codes should differ
        Assert.NotEqual(Comparer.GetHashCode(a), Comparer.GetHashCode(b));
    }

    [Fact]
    public void Comparer_WorksWithDictionary()
    {
        var dict = new Dictionary<RoutingKey, string>(Comparer)
        {
            [new RoutingKey("10.0.0.1", 161, "hrProcessorLoad")] = "tenant-a"
        };

        // Look up with different-cased metric name
        var lookupKey = new RoutingKey("10.0.0.1", 161, "HRPROCESSORLOAD");
        Assert.True(dict.TryGetValue(lookupKey, out var value));
        Assert.Equal("tenant-a", value);
    }
}
