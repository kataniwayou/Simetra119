using Microsoft.Extensions.Logging;
using NSubstitute;
using SnmpCollector.Configuration;
using SnmpCollector.Configuration.Validators;
using SnmpCollector.Pipeline;
using Xunit;

namespace SnmpCollector.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="TenantVectorOptionsValidator"/>.
/// Covers all validation rules with positive and negative cases.
/// Uses NSubstitute to mock IOidMapService.
/// </summary>
public class TenantVectorOptionsValidatorTests
{
    private readonly IOidMapService _oidMapService;
    private readonly TenantVectorOptionsValidator _validator;

    public TenantVectorOptionsValidatorTests()
    {
        _oidMapService = Substitute.For<IOidMapService>();
        _oidMapService.ContainsMetricName(Arg.Any<string>()).Returns(true);
        _oidMapService.EntryCount.Returns(92);

        var logger = Substitute.For<ILogger<TenantVectorOptionsValidator>>();
        _validator = new TenantVectorOptionsValidator(_oidMapService, logger);
    }

    private static TenantVectorOptions ValidOptions() => new()
    {
        Tenants =
        [
            new TenantOptions
            {
                Priority = 1,
                Metrics =
                [
                    new MetricSlotOptions
                    {
                        Ip = "10.0.0.1",
                        Port = 161,
                        MetricName = "obp_link_state_L1"
                    }
                ]
            }
        ]
    };

    // ========== Positive cases ==========

    [Fact]
    public void Validate_ValidConfig_ReturnsSuccess()
    {
        var result = _validator.Validate(null, ValidOptions());
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_MultipleTenants_ReturnsSuccess()
    {
        var options = ValidOptions();
        options.Tenants.Add(new TenantOptions
        {
            Priority = 2,
            Metrics =
            [
                new MetricSlotOptions
                {
                    Ip = "10.0.0.2",
                    Port = 161,
                    MetricName = "npb_cpu_util",

                }
            ]
        });

        var result = _validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_EmptyMetricsArray_ReturnsSuccess()
    {
        var options = new TenantVectorOptions
        {
            Tenants =
            [
                new TenantOptions
                {
                    Priority = 1,
                    Metrics = []
                }
            ]
        };

        var result = _validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_CrossTenantOverlap_ReturnsSuccess()
    {
        var sharedMetric = new MetricSlotOptions
        {
            Ip = "10.0.0.1",
            Port = 161,
            MetricName = "obp_link_state_L1",

        };

        var options = new TenantVectorOptions
        {
            Tenants =
            [
                new TenantOptions
                {
                    Priority = 1,
                    Metrics = [sharedMetric]
                },
                new TenantOptions
                {
                    Priority = 2,
                    Metrics =
                    [
                        new MetricSlotOptions
                        {
                            Ip = "10.0.0.1",
                            Port = 161,
                            MetricName = "obp_link_state_L1",
                
                        }
                    ]
                }
            ]
        };

        var result = _validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_EmptyTenantsList_ReturnsSuccess()
    {
        var options = new TenantVectorOptions { Tenants = [] };
        var result = _validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_NegativePriority_ReturnsSuccess()
    {
        var options = ValidOptions();
        options.Tenants[0].Priority = -100;

        var result = _validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }

    // ========== Negative cases ==========

    [Fact]
    public void Validate_InvalidIpAddress_Fails()
    {
        var options = ValidOptions();
        options.Tenants[0].Metrics[0].Ip = "not-an-ip";

        var result = _validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("not a valid IP address"));
    }

    [Fact]
    public void Validate_EmptyIpAddress_Fails()
    {
        var options = ValidOptions();
        options.Tenants[0].Metrics[0].Ip = "";

        var result = _validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("Ip is required"));
    }

    [Fact]
    public void Validate_PortZero_Fails()
    {
        var options = ValidOptions();
        options.Tenants[0].Metrics[0].Port = 0;

        var result = _validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("Port must be between 1 and 65535"));
    }

    [Fact]
    public void Validate_PortAbove65535_Fails()
    {
        var options = ValidOptions();
        options.Tenants[0].Metrics[0].Port = 70000;

        var result = _validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("Port must be between 1 and 65535"));
    }

    [Fact]
    public void Validate_EmptyMetricName_Fails()
    {
        var options = ValidOptions();
        options.Tenants[0].Metrics[0].MetricName = "";

        var result = _validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("MetricName is required"));
    }

    [Fact]
    public void Validate_MetricNameNotInOidMap_Fails()
    {
        _oidMapService.ContainsMetricName("nonexistent_metric").Returns(false);

        var options = ValidOptions();
        options.Tenants[0].Metrics[0].MetricName = "nonexistent_metric";

        var result = _validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("not found in OID map"));
    }

    [Fact]
    public void Validate_DuplicateMetricWithinTenant_Fails()
    {
        var options = ValidOptions();
        options.Tenants[0].Metrics.Add(new MetricSlotOptions
        {
            Ip = "10.0.0.1",
            Port = 161,
            MetricName = "obp_link_state_L1"
        });

        var result = _validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("duplicate metric slot"));
    }

    [Fact]
    public void Validate_MultipleErrorsCollected_ReportsAll()
    {
        var options = new TenantVectorOptions
        {
            Tenants =
            [
                new TenantOptions
                {
                    Priority = 1,
                    Metrics =
                    [
                        new MetricSlotOptions
                        {
                            Ip = "",           // Error 1: empty IP
                            Port = 0,          // Error 2: invalid port
                            MetricName = ""    // Error 3: empty metric name
                        }
                    ]
                }
            ]
        };

        var result = _validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.True(result.Failures.Count() >= 3, $"Expected at least 3 errors but got {result.Failures.Count()}");
    }

    // ========== OID map empty cases ==========

    [Fact]
    public void Validate_OidMapEmpty_SkipsMetricNameCheck_ReturnsSuccess()
    {
        _oidMapService.EntryCount.Returns(0);
        _oidMapService.ContainsMetricName(Arg.Any<string>()).Returns(false);

        var options = ValidOptions();

        var result = _validator.Validate(null, options);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_OidMapEmpty_StillValidatesOtherRules()
    {
        _oidMapService.EntryCount.Returns(0);

        var options = ValidOptions();
        options.Tenants[0].Metrics[0].Ip = "not-valid";

        var result = _validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("not a valid IP address"));
    }

    // ========== Error message format ==========

    [Fact]
    public void Validate_ErrorMessages_ContainPathContext()
    {
        var options = ValidOptions();
        options.Tenants[0].Metrics[0].Port = 0;

        var result = _validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("Tenants[0].Metrics[0]"));
    }
}
