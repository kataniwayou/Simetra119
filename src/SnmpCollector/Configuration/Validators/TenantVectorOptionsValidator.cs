using Microsoft.Extensions.Options;

namespace SnmpCollector.Configuration.Validators;

/// <summary>
/// No-op validator for <see cref="TenantVectorOptions"/>. Tenant creation is unconditional;
/// the operator is responsible for correct config. Data arrives only if ip/port/metric_name
/// matches at runtime.
/// </summary>
public sealed class TenantVectorOptionsValidator : IValidateOptions<TenantVectorOptions>
{
    public ValidateOptionsResult Validate(string? name, TenantVectorOptions options)
    {
        return ValidateOptionsResult.Success;
    }
}
