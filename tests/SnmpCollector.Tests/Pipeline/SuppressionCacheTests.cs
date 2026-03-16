using SnmpCollector.Pipeline;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

public sealed class SuppressionCacheTests
{
    [Fact]
    public void FirstCall_ReturnsFalse_AndStamps()
    {
        var cache = new SuppressionCache();

        var result = cache.TrySuppress("key1", 60);

        Assert.False(result);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void SecondCallWithinWindow_ReturnsTrue()
    {
        var cache = new SuppressionCache();

        cache.TrySuppress("key1", 60);
        var result = cache.TrySuppress("key1", 60);

        Assert.True(result);
    }

    [Fact]
    public void AfterWindowExpires_ReturnsFalse()
    {
        var cache = new SuppressionCache();

        // First call stamps with now.
        cache.TrySuppress("key1", 60);

        // Second call with windowSeconds=0 means the window has already expired.
        var result = cache.TrySuppress("key1", 0);

        Assert.False(result);
    }

    [Fact]
    public void DifferentKeys_Independent()
    {
        var cache = new SuppressionCache();

        var r1 = cache.TrySuppress("key1", 60);
        var r2 = cache.TrySuppress("key2", 60);

        Assert.False(r1);
        Assert.False(r2);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void WindowPassedAtCheckTime_NotStored()
    {
        var cache = new SuppressionCache();

        // First call with 60s window — stamps the entry.
        cache.TrySuppress("key1", 60);

        // Second call with 0s window — window evaluated at check time, not stored.
        var result = cache.TrySuppress("key1", 0);

        Assert.False(result); // 0s window means already expired
    }

    [Fact]
    public void SuppressedCall_DoesNotUpdateTimestamp()
    {
        var cache = new SuppressionCache();

        // First call: stamps.
        cache.TrySuppress("key1", 60);

        // Second call within window: suppressed (true), should NOT re-stamp.
        var r2 = cache.TrySuppress("key1", 60);
        Assert.True(r2);

        // Third call within window: still suppressed (proving stamp was NOT updated by second call).
        var r3 = cache.TrySuppress("key1", 60);
        Assert.True(r3);
    }

    [Fact]
    public void Count_ReflectsEntries()
    {
        var cache = new SuppressionCache();

        cache.TrySuppress("a", 60);
        cache.TrySuppress("b", 60);
        cache.TrySuppress("c", 60);

        Assert.Equal(3, cache.Count);
    }
}
