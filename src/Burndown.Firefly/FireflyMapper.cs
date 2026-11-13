using System.Globalization;
using System.Text.Json;

namespace Burndown.Firefly;

/// <summary>Converts between the Firefly wire format and the typed models.</summary>
internal static class FireflyMapper
{
    public const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:sszzz";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ToWire(AccountKind kind) =>
        kind switch
        {
            AccountKind.Asset => "asset",
            AccountKind.Cash => "cash",
            AccountKind.Expense => "expense",
            AccountKind.Revenue => "revenue",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown account kind."),
        };

    public static string ToWire(TransactionType type) =>
        type switch
        {
            TransactionType.Withdrawal => "withdrawal",
            TransactionType.Deposit => "deposit",
            TransactionType.Transfer => "transfer",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown transaction type."),
        };

    /// <summary>Parses a transaction type. Types the app does not handle (opening balance, ...) return false.</summary>
    /// <param name="wire">The Firefly type string.</param>
    /// <param name="type">The parsed type when the method returns true.</param>
    /// <returns>True when the type is one the app handles.</returns>
    public static bool TryParseType(string? wire, out TransactionType type)
    {
        switch (wire)
        {
            case "withdrawal":
                type = TransactionType.Withdrawal;
                return true;
            case "deposit":
                type = TransactionType.Deposit;
                return true;
            case "transfer":
                type = TransactionType.Transfer;
                return true;
            default:
                type = default;
                return false;
        }
    }

    public static AccountId? ParseAccountId(string? wire) =>
        int.TryParse(wire, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && AccountId.TryFrom(value, out var id)
            ? id
            : null;

    public static BudgetId? ParseBudgetId(string? wire) =>
        int.TryParse(wire, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && BudgetId.TryFrom(value, out var id)
            ? id
            : null;

    public static TransactionId? ParseTransactionId(string? wire) =>
        int.TryParse(wire, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && TransactionId.TryFrom(value, out var id)
            ? id
            : null;

    public static FireflyAccount? MapAccount(ResourceDto<AccountAttributesDto> resource, AccountKind kind) =>
        ParseAccountId(resource.Id) is { } id && resource.Attributes?.Name is { Length: > 0 } name
            ? new FireflyAccount(id, name, kind, resource.Attributes.Active ?? true)
            : null;

    public static FireflyBudget? MapBudget(ResourceDto<BudgetAttributesDto> resource) =>
        ParseBudgetId(resource.Id) is { } id && resource.Attributes?.Name is { Length: > 0 } name
            ? new FireflyBudget(id, name, resource.Attributes.Active ?? true)
            : null;

    /// <summary>Maps one split. Returns null when a required field is missing or malformed.</summary>
    /// <param name="groupId">The id of the transaction group the split belongs to.</param>
    /// <param name="split">The wire split.</param>
    /// <returns>The mapped transaction, or null.</returns>
    public static FireflyTransaction? MapSplit(TransactionId groupId, TransactionSplitDto split)
    {
        if (
            !TryParseType(split.Type, out var type)
            || !long.TryParse(
                split.TransactionJournalId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var journalId
            )
            || !DateTimeOffset.TryParse(split.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || !decimal.TryParse(split.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            || ParseAccountId(split.SourceId) is not { } sourceId
            || ParseAccountId(split.DestinationId) is not { } destinationId
            || split.SourceType is null
            || split.DestinationType is null
        )
        {
            return null;
        }

        BudgetId? budgetId = null;
        if (!string.IsNullOrEmpty(split.BudgetId) && split.BudgetId != "0")
        {
            budgetId = ParseBudgetId(split.BudgetId);
            if (budgetId is null)
            {
                return null;
            }
        }

        return new FireflyTransaction(
            groupId,
            journalId,
            type,
            date,
            amount,
            split.Description ?? string.Empty,
            sourceId,
            split.SourceType,
            destinationId,
            split.DestinationType,
            budgetId,
            string.IsNullOrEmpty(split.CategoryName) ? null : split.CategoryName,
            split.Tags ?? []
        );
    }

    public static CreateTransactionRequest ToRequest(NewTransaction transaction) =>
        new(
            ApplyRules: true,
            FireWebhooks: true,
            ErrorIfDuplicateHash: false,
            [
                new NewTransactionSplitDto(
                    ToWire(transaction.Type),
                    transaction.Date.ToString(DateTimeFormat, CultureInfo.InvariantCulture),
                    transaction.Amount.ToString(CultureInfo.InvariantCulture),
                    transaction.Description,
                    transaction.SourceId.Value.ToString(CultureInfo.InvariantCulture),
                    transaction.DestinationId.Value.ToString(CultureInfo.InvariantCulture),
                    transaction.Category,
                    transaction.BudgetId?.Value.ToString(CultureInfo.InvariantCulture),
                    [.. transaction.Tags]
                ),
            ]
        );
}
