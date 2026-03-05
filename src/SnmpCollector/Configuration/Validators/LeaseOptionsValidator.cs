using Microsoft.Extensions.Options;

namespace SnmpCollector.Configuration.Validators;

/// <summary>
/// Validates cross-field constraints on <see cref="LeaseOptions"/> that DataAnnotations cannot express.
/// DurationSeconds must be greater than RenewIntervalSeconds to ensure the leader has time to renew
/// before the lease expires and another instance attempts acquisition.
/// </summary>
public sealed class LeaseOptionsValidator : IValidateOptions<LeaseOptions>
{
    public ValidateOptionsResult Validate(string? name, LeaseOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Name))
            failures.Add("Lease:Name must not be empty or whitespace");

        if (string.IsNullOrWhiteSpace(options.Namespace))
            failures.Add("Lease:Namespace must not be empty or whitespace");

        if (options.DurationSeconds <= options.RenewIntervalSeconds)
            failures.Add("Lease:DurationSeconds must be greater than Lease:RenewIntervalSeconds");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
