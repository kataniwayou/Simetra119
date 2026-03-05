using SnmpCollector.Pipeline;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

public sealed class JobIntervalRegistryTests
{
    [Fact]
    public void Register_TryGetInterval_RetrievesValue()
    {
        var registry = new JobIntervalRegistry();
        registry.Register("correlation", 30);

        var found = registry.TryGetInterval("correlation", out var interval);

        Assert.True(found);
        Assert.Equal(30, interval);
    }

    [Fact]
    public void TryGetInterval_ReturnsFalse_ForUnknownKey()
    {
        var registry = new JobIntervalRegistry();

        var found = registry.TryGetInterval("nonexistent", out _);

        Assert.False(found);
    }

    [Fact]
    public void Register_OverwritesPreviousInterval()
    {
        var registry = new JobIntervalRegistry();
        registry.Register("job-a", 30);
        registry.Register("job-a", 60);

        registry.TryGetInterval("job-a", out var interval);

        Assert.Equal(60, interval);
    }

    [Fact]
    public void MultipleRegistrations_AreIndependent()
    {
        var registry = new JobIntervalRegistry();
        registry.Register("correlation", 30);
        registry.Register("metric-poll-switch1-0", 60);
        registry.Register("metric-poll-switch2-0", 120);

        registry.TryGetInterval("correlation", out var c);
        registry.TryGetInterval("metric-poll-switch1-0", out var p1);
        registry.TryGetInterval("metric-poll-switch2-0", out var p2);

        Assert.Equal(30, c);
        Assert.Equal(60, p1);
        Assert.Equal(120, p2);
    }
}
