using Microsoft.Extensions.Options;

namespace SnmpCollector.Configuration.Validators;

/// <summary>
/// Validates <see cref="SnmpListenerOptions"/> at startup.
/// </summary>
public sealed class SnmpListenerOptionsValidator : IValidateOptions<SnmpListenerOptions>
{
    public ValidateOptionsResult Validate(string? name, SnmpListenerOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BindAddress))
        {
            failures.Add("SnmpListener:BindAddress is required");
        }

        if (string.IsNullOrWhiteSpace(options.CommunityString))
        {
            failures.Add("SnmpListener:CommunityString is required");
        }

        if (options.Version != "v2c")
        {
            failures.Add("SnmpListener:Version must be 'v2c'");
        }

        if (options.Port < 1 || options.Port > 65535)
        {
            failures.Add("SnmpListener:Port must be between 1 and 65535");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
