using Microsoft.Extensions.Options;

namespace Burndown.Overview;

/// <summary>Validates <see cref="OverviewOptions"/> without touching the network.</summary>
internal sealed class OverviewOptionsValidator : IValidateOptions<OverviewOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OverviewOptions options)
    {
        var failures = new List<string>();

        if (!options.TryGetMinimumMonth(out _))
        {
            failures.Add(
                $"{OverviewOptions.Section}:{nameof(OverviewOptions.MinimumMonth)} must be formatted as yyyy-MM."
            );
        }

        if (string.IsNullOrWhiteSpace(options.MonthlyInstallmentTag))
        {
            failures.Add($"{OverviewOptions.Section}:{nameof(OverviewOptions.MonthlyInstallmentTag)} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ExcludedDestinationAccountType))
        {
            failures.Add(
                $"{OverviewOptions.Section}:{nameof(OverviewOptions.ExcludedDestinationAccountType)} is required."
            );
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
