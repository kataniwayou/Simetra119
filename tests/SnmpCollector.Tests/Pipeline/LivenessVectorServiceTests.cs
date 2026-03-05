using SnmpCollector.Pipeline;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

public sealed class LivenessVectorServiceTests
{
    [Fact]
    public void Stamp_RecordsTimestamp_GetStampReturnsIt()
    {
        var service = new LivenessVectorService();
        var before = DateTimeOffset.UtcNow;

        service.Stamp("test-job");

        var stamp = service.GetStamp("test-job");
        Assert.NotNull(stamp);
        Assert.True(stamp >= before);
        Assert.True(stamp <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void GetStamp_ReturnsNull_ForUnknownKey()
    {
        var service = new LivenessVectorService();

        Assert.Null(service.GetStamp("nonexistent"));
    }

    [Fact]
    public async Task Stamp_OverwritesPreviousValue()
    {
        var service = new LivenessVectorService();
        service.Stamp("job-a");
        var first = service.GetStamp("job-a");

        await Task.Delay(10);
        service.Stamp("job-a");
        var second = service.GetStamp("job-a");

        Assert.True(second > first);
    }

    [Fact]
    public void GetAllStamps_ReturnsDefensiveCopy()
    {
        var service = new LivenessVectorService();
        service.Stamp("job-1");
        service.Stamp("job-2");

        var snapshot = service.GetAllStamps();

        Assert.Equal(2, snapshot.Count);
        Assert.True(snapshot.ContainsKey("job-1"));
        Assert.True(snapshot.ContainsKey("job-2"));

        // Mutating after snapshot should not affect snapshot
        service.Stamp("job-3");
        Assert.Equal(2, snapshot.Count);
    }

    [Fact]
    public void GetAllStamps_ReturnsEmpty_WhenNoStamps()
    {
        var service = new LivenessVectorService();

        var stamps = service.GetAllStamps();

        Assert.Empty(stamps);
    }
}
