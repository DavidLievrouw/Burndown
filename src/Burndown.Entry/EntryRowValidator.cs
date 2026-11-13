using System.Globalization;
using Burndown.Common;
using Burndown.Firefly;
using Microsoft.Extensions.Options;

namespace Burndown.Entry;

/// <summary>A row that passed validation, ready to be sequenced and sent.</summary>
/// <param name="Type">The row type, see <see cref="RowTypes"/>.</param>
/// <param name="Local">The local date and time as entered, minute precision, of unspecified kind.</param>
/// <param name="Amount">The amount.</param>
/// <param name="Description">The trimmed description.</param>
/// <param name="Source">The source account.</param>
/// <param name="Destination">The destination account.</param>
/// <param name="Category">The configured spelling of the category, for an expense.</param>
/// <param name="Tag">The configured spelling of the target tag, for an expense.</param>
/// <param name="Budget">The budget, for an expense that has one.</param>
/// <param name="ChosenAccounts">The accounts the user chose, which the usage tracker ranks.</param>
internal sealed record ValidRow(
    string Type,
    DateTime Local,
    decimal Amount,
    string Description,
    AccountId Source,
    AccountId Destination,
    string? Category,
    string? Tag,
    BudgetId? Budget,
    IReadOnlyList<int> ChosenAccounts
);

/// <summary>The server-authoritative validation of one row, the same rules the grid shows inline.</summary>
internal sealed class EntryRowValidator
{
    /// <summary>The longest description Firefly accepts.</summary>
    public const int MaxDescriptionLength = 1000;

    /// <summary>The largest amount that is accepted.</summary>
    public const decimal MaxAmount = 999_999_999.99m;

    /// <summary>The format of <see cref="SubmitRow.DateTime"/>.</summary>
    public const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm";

    private readonly EntryOptions _options;

    /// <summary>Initializes a new instance of the <see cref="EntryRowValidator"/> class.</summary>
    /// <param name="options">The Entry options.</param>
    public EntryRowValidator(IOptions<EntryOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Validates a row.</summary>
    /// <param name="row">The row.</param>
    /// <param name="today">The current date in the configured zone, which bounds the allowed dates.</param>
    /// <returns>The valid row, or a validation failure with per-field messages.</returns>
    public Result<ValidRow> Validate(SubmitRow row, DateOnly today)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var type = row.Type;
        if (type is not (RowTypes.Expense or RowTypes.Income or RowTypes.Transfer))
        {
            fields["type"] = [$"The type must be {RowTypes.Expense}, {RowTypes.Income} or {RowTypes.Transfer}."];
        }

        var local = ParseDateTime(row.DateTime, today, fields);
        var description = ValidateDescription(row.Description, fields);
        var amount = ValidateAmount(row.Amount, fields);

        AccountId? source = null;
        AccountId? destination = null;
        var chosen = new List<int>();
        string? category = null;
        string? tag = null;
        BudgetId? budget = null;
        var cash = AccountId.From(_options.CashAccountId);

        switch (type)
        {
            case RowTypes.Expense:
                source = ValidateAccount("accountId", row.AccountId, fields);
                destination = cash;
                category = ValidateChoice("category", row.Category, _options.Categories, fields);
                tag = ValidateChoice("tag", row.Tag, _options.TargetTags, fields);
                budget = ValidateBudget(row.BudgetId, fields);
                AddChosen(chosen, source);
                break;
            case RowTypes.Income:
                source = cash;
                destination = ValidateAccount("accountId", row.AccountId, fields);
                AddChosen(chosen, destination);
                break;
            case RowTypes.Transfer:
                source = ValidateAccount("fromAccountId", row.FromAccountId, fields);
                destination = ValidateAccount("toAccountId", row.ToAccountId, fields);
                if (source is { } from && destination is { } to && from == to)
                {
                    fields["toAccountId"] = ["The accounts of a transfer must be different."];
                }

                AddChosen(chosen, source);
                AddChosen(chosen, destination);
                break;
        }

        if (fields.Count > 0)
        {
            return Result<ValidRow>.Failure(new Error(ErrorCodes.Validation, "The row is not valid.", fields));
        }

        return Result<ValidRow>.Success(
            new ValidRow(
                type!,
                local!.Value,
                amount!.Value,
                description!,
                source!.Value,
                destination!.Value,
                category,
                tag,
                budget,
                chosen
            )
        );
    }

    private static void AddChosen(List<int> chosen, AccountId? account)
    {
        if (account is { } value)
        {
            chosen.Add(value.Value);
        }
    }

    private DateTime? ParseDateTime(string? text, DateOnly today, Dictionary<string, string[]> fields)
    {
        if (
            text is null
            || !DateTime.TryParseExact(
                text,
                EntryRowValidator.DateTimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var local
            )
        )
        {
            fields["dateTime"] = [$"The date and time must be formatted as {EntryRowValidator.DateTimeFormat}."];
            return null;
        }

        var date = DateOnly.FromDateTime(local);
        var min = EntryDates.MinDate(today, _options);
        var max = EntryDates.MaxDate(today);
        if (date < min || date > max)
        {
            fields["dateTime"] =
            [
                $"The date must be between {min.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} and {max.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.",
            ];
            return null;
        }

        return local;
    }

    private static string? ValidateDescription(string? text, Dictionary<string, string[]> fields)
    {
        var description = text?.Trim();
        if (string.IsNullOrEmpty(description))
        {
            fields["description"] = ["A description is required."];
            return null;
        }

        if (description.Length > MaxDescriptionLength)
        {
            fields["description"] = [$"The description can have at most {MaxDescriptionLength} characters."];
            return null;
        }

        return description;
    }

    private static decimal? ValidateAmount(decimal? amount, Dictionary<string, string[]> fields)
    {
        if (amount is not { } value || value <= 0)
        {
            fields["amount"] = ["The amount must be greater than zero."];
            return null;
        }

        if (decimal.Round(value, 2) != value)
        {
            fields["amount"] = ["The amount can have at most 2 decimals."];
            return null;
        }

        if (value > MaxAmount)
        {
            fields["amount"] =
            [
                $"The amount can be at most {MaxAmount.ToString("0.00", CultureInfo.InvariantCulture)}.",
            ];
            return null;
        }

        return value;
    }

    private AccountId? ValidateAccount(string field, int? id, Dictionary<string, string[]> fields)
    {
        if (id is not { } value || value <= 0)
        {
            fields[field] = ["Choose an account."];
            return null;
        }

        if (value == _options.CashAccountId)
        {
            fields[field] = ["The cash account cannot be chosen: pick an asset account."];
            return null;
        }

        return AccountId.From(value);
    }

    private static string? ValidateChoice(
        string field,
        string? text,
        IReadOnlyList<string> allowed,
        Dictionary<string, string[]> fields
    )
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            fields[field] = ["A value is required."];
            return null;
        }

        var match = allowed.FirstOrDefault(a => string.Equals(a, trimmed, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            fields[field] = ["Choose one of the listed values."];
        }

        return match;
    }

    private BudgetId? ValidateBudget(int? id, Dictionary<string, string[]> fields)
    {
        if (id is not { } value)
        {
            return null;
        }

        if (_options.Budgets.All(b => b.Id != value))
        {
            fields["budgetId"] = ["Choose one of the listed budgets."];
            return null;
        }

        return BudgetId.From(value);
    }
}
