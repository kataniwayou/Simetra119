using Microsoft.Extensions.Options;
using SnmpCollector.Configuration;

namespace SnmpCollector.Configuration.Validators;

/// <summary>
/// Validates <see cref="OtlpOptions"/> at startup.
/// </summary>
public sealed class OtlpOptionsValidator : IValidateOptions<OtlpOptions>
{
    public ValidateOptionsResult Validate(string? name, OtlpOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            failures.Add("Otlp:Endpoint is required");
        }

        if (string.IsNullOrWhiteSpace(options.ServiceName))
        {
            failures.Add("Otlp:ServiceName is required");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
