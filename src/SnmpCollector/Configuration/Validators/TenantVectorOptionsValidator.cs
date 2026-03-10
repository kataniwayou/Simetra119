using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnmpCollector.Pipeline;

namespace SnmpCollector.Configuration.Validators;

/// <summary>
/// Validates <see cref="TenantVectorOptions"/> at startup.
/// Collects all errors into a list (never short-circuits on first failure)
/// so operators see every config problem in one pass.
/// Injects <see cref="IOidMapService"/> to verify MetricName references exist in the OID map.
/// </summary>
public sealed class TenantVectorOptionsValidator : IValidateOptions<TenantVectorOptions>
{
    private readonly IOidMapService _oidMapService;
    private readonly ILogger<TenantVectorOptionsValidator> _logger;

    public TenantVectorOptionsValidator(
        IOidMapService oidMapService,
        ILogger<TenantVectorOptionsValidator> logger)
    {
        _oidMapService = oidMapService;
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, TenantVectorOptions options)
    {
        var failures = new List<string>();

        // Track whether OID map is empty (skip MetricName OID map checks if so)
        var oidMapEmpty = _oidMapService.EntryCount == 0;
        var loggedOidMapWarning = false;

        // Duplicate tenant ID detection (case-insensitive)
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < options.Tenants.Count; i++)
        {
            var tenant = options.Tenants[i];

            // Rule 1: Tenant Id required
            if (string.IsNullOrWhiteSpace(tenant.Id))
            {
                failures.Add($"Tenants[{i}].Id is required");
            }
            else
            {
                // Rule 2: Duplicate tenant IDs (only check non-empty IDs)
                if (!seenIds.Add(tenant.Id))
                {
                    failures.Add($"Tenants[{i}].Id '{tenant.Id}' is a duplicate (case-insensitive)");
                }
            }

            // Per-tenant duplicate metric detection
            var seenMetrics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var j = 0; j < tenant.Metrics.Count; j++)
            {
                var metric = tenant.Metrics[j];
                var prefix = $"Tenants[{i}].Metrics[{j}]";

                // Rule 3: IP required and valid
                if (string.IsNullOrWhiteSpace(metric.Ip))
                {
                    failures.Add($"{prefix}.Ip is required");
                }
                else if (!IPAddress.TryParse(metric.Ip, out _))
                {
                    failures.Add($"{prefix}.Ip '{metric.Ip}' is not a valid IP address");
                }

                // Rule 4: Port range
                if (metric.Port < 1 || metric.Port > 65535)
                {
                    failures.Add($"{prefix}.Port must be between 1 and 65535");
                }

                // Rule 5: MetricName required
                if (string.IsNullOrWhiteSpace(metric.MetricName))
                {
                    failures.Add($"{prefix}.MetricName is required");
                }
                else
                {
                    // Rule 6: MetricName in OID map (skip if map is empty)
                    if (oidMapEmpty)
                    {
                        if (!loggedOidMapWarning)
                        {
                            _logger.LogWarning(
                                "OID map is empty — skipping MetricName validation (map may not be loaded yet)");
                            loggedOidMapWarning = true;
                        }
                    }
                    else if (!_oidMapService.ContainsMetricName(metric.MetricName))
                    {
                        failures.Add($"{prefix}.MetricName '{metric.MetricName}' not found in OID map");
                    }
                }

                // Rule 7: Per-tenant duplicate metrics (ip:port:metric_name)
                if (!string.IsNullOrWhiteSpace(metric.Ip) && !string.IsNullOrWhiteSpace(metric.MetricName))
                {
                    var key = $"{metric.Ip}:{metric.Port}:{metric.MetricName}";
                    if (!seenMetrics.Add(key))
                    {
                        failures.Add($"{prefix} is a duplicate metric slot ({key}) within tenant '{tenant.Id}'");
                    }
                }
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
