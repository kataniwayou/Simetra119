using SnmpCollector.Pipeline;
using SnmpCollector.Configuration;
using System.Text.Json;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

public class AggregatedMetricModelTests
{
    [Fact]
    public void AggregationKind_HasFourMembers()
    {
        var values = Enum.GetValues<AggregationKind>();
        Assert.Equal(4, values.Length);
        Assert.Contains(AggregationKind.Sum, values);
        Assert.Contains(AggregationKind.Subtract, values);
        Assert.Contains(AggregationKind.AbsDiff, values);
        Assert.Contains(AggregationKind.Mean, values);
    }

    [Theory]
    [InlineData("sum", AggregationKind.Sum)]
    [InlineData("subtract", AggregationKind.Subtract)]
    [InlineData("absDiff", AggregationKind.AbsDiff)]
    [InlineData("mean", AggregationKind.Mean)]
    public void AggregationKind_TryParseLowercase_Succeeds(string input, AggregationKind expected)
    {
        var result = Enum.TryParse<AggregationKind>(input, ignoreCase: true, out var kind);
        Assert.True(result);
        Assert.Equal(expected, kind);
    }

    [Fact]
    public void AggregationKind_TryParseInvalid_ReturnsFalse()
    {
        var result = Enum.TryParse<AggregationKind>("invalid", ignoreCase: true, out _);
        Assert.False(result);
    }

    [Fact]
    public void AggregatedMetricDefinition_PositionalConstruction_PropertiesAccessible()
    {
        var sourceOids = new[] { "1.3.6.1.2.1.1.0", "1.3.6.1.2.1.2.0" };
        var definition = new AggregatedMetricDefinition("test_aggregated", AggregationKind.Sum, sourceOids);

        Assert.Equal("test_aggregated", definition.MetricName);
        Assert.Equal(AggregationKind.Sum, definition.Kind);
        Assert.Equal(sourceOids, definition.SourceOids);
    }

    [Fact]
    public void AggregatedMetricDefinition_RecordEquality_Works()
    {
        IReadOnlyList<string> oids = new[] { "1.3.6.1.2.1.1.0", "1.3.6.1.2.1.2.0" };
        var a = new AggregatedMetricDefinition("test_aggregated", AggregationKind.Sum, oids);
        var b = new AggregatedMetricDefinition("test_aggregated", AggregationKind.Sum, oids);

        Assert.Equal(a, b);
    }

    [Fact]
    public void MetricPollInfo_ExistingConstruction_StillCompiles()
    {
        var info = new MetricPollInfo(
            PollIndex: 0,
            Oids: new[] { "1.3.6.1.2.1.1.0" },
            IntervalSeconds: 10,
            TimeoutMultiplier: 0.8);

        Assert.NotNull(info.AggregatedMetrics);
        Assert.Empty(info.AggregatedMetrics);
    }

    [Fact]
    public void MetricPollInfo_WithAggregatedMetrics_Populates()
    {
        var definition = new AggregatedMetricDefinition(
            "test_aggregated",
            AggregationKind.Sum,
            new[] { "1.3.6.1.2.1.1.0", "1.3.6.1.2.1.2.0" });

        var info = new MetricPollInfo(
            PollIndex: 0,
            Oids: new[] { "1.3.6.1.2.1.1.0", "1.3.6.1.2.1.2.0" },
            IntervalSeconds: 10)
        {
            AggregatedMetrics = new[] { definition }
        };

        var single = Assert.Single(info.AggregatedMetrics);
        Assert.Equal("test_aggregated", single.MetricName);
    }

    [Fact]
    public void MetricPollInfo_DefaultAggregatedMetrics_IsEmptyNotNull()
    {
        var info = new MetricPollInfo(
            PollIndex: 1,
            Oids: new[] { "1.3.6.1.2.1.1.0" },
            IntervalSeconds: 30,
            TimeoutMultiplier: 0.8);

        Assert.NotNull(info.AggregatedMetrics);
        Assert.Empty(info.AggregatedMetrics);
    }

    [Fact]
    public void PollOptions_Deserialization_WithoutAggregation_DefaultsNull()
    {
        const string json = """{"MetricNames":["m1"],"IntervalSeconds":10}""";
        var options = JsonSerializer.Deserialize<PollOptions>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(options);
        Assert.Null(options.AggregatedMetricName);
        Assert.Null(options.Aggregator);
    }

    [Fact]
    public void PollOptions_Deserialization_WithAggregation_BothPopulated()
    {
        const string json = """{"MetricNames":["m1","m2"],"IntervalSeconds":10,"AggregatedMetricName":"combined","Aggregator":"sum"}""";
        var options = JsonSerializer.Deserialize<PollOptions>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(options);
        Assert.Equal("combined", options.AggregatedMetricName);
        Assert.Equal("sum", options.Aggregator);
    }
}
