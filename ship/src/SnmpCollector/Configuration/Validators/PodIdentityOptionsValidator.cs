using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;

namespace SnmpCollector.Configuration.Validators;

/// <summary>
/// Validates <see cref="PodIdentityOptions"/> at startup.
/// </summary>
public sealed class PodIdentityOptionsValidator : IValidateOptions<PodIdentityOptions>
{
    public ValidateOptionsResult Validate(string? name, PodIdentityOptions options)
    {
        // PodIdentity is auto-populated via PostConfigure -- no validation required.
        return ValidateOptionsResult.Success;
    }
}
