using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;

namespace SnmpCollector.Configuration.Validators;

/// <summary>
/// Validates <see cref="SiteOptions"/> at startup.
/// </summary>
public sealed class SiteOptionsValidator : IValidateOptions<SiteOptions>
{
    public ValidateOptionsResult Validate(string? name, SiteOptions options)
    {
        // Name is now optional -- no validation required.
        return ValidateOptionsResult.Success;
    }
}
