using Microsoft.Extensions.Options;

namespace Burndown.Entry;

/// <summary>Validates <see cref="EntryOptions"/> without touching the network.</summary>
internal sealed class EntryOptionsValidator : IValidateOptions<EntryOptions>
{
    private const int MaxBackdatedMonthsLimit = 120;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, EntryOptions options)
    {
        var failures = new List<string>();

        if (options.CashAccountId <= 0)
        {
            failures.Add($"{EntryOptions.Section}:{nameof(EntryOptions.CashAccountId)} must be a positive id.");
        }

        if (options.MaxBackdatedMonths is < 0 or > MaxBackdatedMonthsLimit)
        {
            failures.Add(
                $"{EntryOptions.Section}:{nameof(EntryOptions.MaxBackdatedMonths)} must be between 0 and {MaxBackdatedMonthsLimit}."
            );
        }

        ValidateNames(failures, nameof(EntryOptions.Categories), options.Categories);
        ValidateNames(failures, nameof(EntryOptions.TargetTags), options.TargetTags);
        ValidateBudgets(failures, options.Budgets);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateNames(List<string> failures, string setting, List<string> values)
    {
        if (values.Count == 0)
        {
            failures.Add($"{EntryOptions.Section}:{setting} must contain at least one value.");
            return;
        }

        if (values.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add($"{EntryOptions.Section}:{setting} must not contain empty values.");
        }

        if (HasDuplicates(values))
        {
            failures.Add($"{EntryOptions.Section}:{setting} must not contain duplicates (ignoring case).");
        }
    }

    private static void ValidateBudgets(List<string> failures, List<BudgetOption> budgets)
    {
        const string setting = nameof(EntryOptions.Budgets);
        if (budgets.Count == 0)
        {
            failures.Add($"{EntryOptions.Section}:{setting} must contain at least one budget.");
            return;
        }

        if (budgets.Any(b => b.Id <= 0))
        {
            failures.Add($"{EntryOptions.Section}:{setting} must only contain positive ids.");
        }

        if (budgets.Select(b => b.Id).Distinct().Count() != budgets.Count)
        {
            failures.Add($"{EntryOptions.Section}:{setting} must not contain duplicate ids.");
        }

        if (budgets.Any(b => string.IsNullOrWhiteSpace(b.Name)))
        {
            failures.Add($"{EntryOptions.Section}:{setting} must not contain empty names.");
        }
        else if (HasDuplicates(budgets.Select(b => b.Name!)))
        {
            failures.Add($"{EntryOptions.Section}:{setting} must not contain duplicate names (ignoring case).");
        }
    }

    private static bool HasDuplicates(IEnumerable<string> values) =>
        values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Count() > 1);
}
