namespace Burndown.Firefly;

/// <summary>The kind of account to list. Firefly lists accounts per kind.</summary>
public enum AccountKind
{
    /// <summary>An asset account (checking, savings, ...).</summary>
    Asset = 0,

    /// <summary>The built-in cash account.</summary>
    Cash = 1,

    /// <summary>An expense account (a payee).</summary>
    Expense = 2,

    /// <summary>A revenue account (a payer).</summary>
    Revenue = 3,
}

/// <summary>The type of a Firefly transaction.</summary>
public enum TransactionType
{
    /// <summary>Money leaves an asset account.</summary>
    Withdrawal = 0,

    /// <summary>Money enters an asset account.</summary>
    Deposit = 1,

    /// <summary>Money moves between two asset accounts.</summary>
    Transfer = 2,
}

/// <summary>Information about the connected Firefly III instance.</summary>
/// <param name="Version">The Firefly III version.</param>
public sealed record FireflyAbout(string Version);

/// <summary>An account.</summary>
/// <param name="Id">The account id.</param>
/// <param name="Name">The account name, as stored in Firefly.</param>
/// <param name="Kind">The kind the account was listed under.</param>
/// <param name="Active">Whether the account is active.</param>
public sealed record FireflyAccount(AccountId Id, string Name, AccountKind Kind, bool Active);

/// <summary>A budget.</summary>
/// <param name="Id">The budget id.</param>
/// <param name="Name">The budget name, as stored in Firefly.</param>
/// <param name="Active">Whether the budget is active.</param>
public sealed record FireflyBudget(BudgetId Id, string Name, bool Active);

/// <summary>One split of a transaction, as read from Firefly. Every split carries its group's id.</summary>
/// <param name="GroupId">The id of the transaction group the split belongs to.</param>
/// <param name="JournalId">The id of the split itself.</param>
/// <param name="Type">The transaction type.</param>
/// <param name="Date">The date and time with the offset Firefly stored it with. Its date part is the calendar date.</param>
/// <param name="Amount">The amount, always positive.</param>
/// <param name="Description">The description.</param>
/// <param name="SourceId">The source account id.</param>
/// <param name="SourceType">The source account type as Firefly words it, for example <c>Asset account</c>.</param>
/// <param name="DestinationId">The destination account id.</param>
/// <param name="DestinationType">
/// The destination account type as Firefly words it, for example <c>Expense account</c>.
/// </param>
/// <param name="BudgetId">The budget id, or <see langword="null"/> when the split has no budget.</param>
/// <param name="Category">The category name, or <see langword="null"/> when the split has no category.</param>
/// <param name="Tags">The tag names.</param>
public sealed record FireflyTransaction(
    TransactionId GroupId,
    long JournalId,
    TransactionType Type,
    DateTimeOffset Date,
    decimal Amount,
    string Description,
    AccountId SourceId,
    string SourceType,
    AccountId DestinationId,
    string DestinationType,
    BudgetId? BudgetId,
    string? Category,
    IReadOnlyList<string> Tags
);

/// <summary>A single-split transaction to create.</summary>
/// <param name="Type">The transaction type.</param>
/// <param name="Date">The date and time, with an explicit offset.</param>
/// <param name="Amount">The amount, positive.</param>
/// <param name="Description">The description.</param>
/// <param name="SourceId">The source account id.</param>
/// <param name="DestinationId">The destination account id.</param>
/// <param name="Category">The category name, or <see langword="null"/> for none.</param>
/// <param name="BudgetId">The budget id, or <see langword="null"/> for none.</param>
/// <param name="Tags">The tag names. Firefly creates tags that do not exist yet.</param>
public sealed record NewTransaction(
    TransactionType Type,
    DateTimeOffset Date,
    decimal Amount,
    string Description,
    AccountId SourceId,
    AccountId DestinationId,
    string? Category,
    BudgetId? BudgetId,
    IReadOnlyList<string> Tags
);

/// <summary>The result of a successful transaction create.</summary>
/// <param name="Id">The id of the new transaction group.</param>
public sealed record CreatedTransaction(TransactionId Id);

/// <summary>Stable error codes of failures that originate in the Firefly client.</summary>
public static class FireflyErrorCodes
{
    /// <summary>Firefly rejected the token (HTTP 401): it is invalid or expired.</summary>
    public const string Unauthorized = "firefly.unauthorized";

    /// <summary>Firefly refused the request (HTTP 403).</summary>
    public const string Forbidden = "firefly.forbidden";

    /// <summary>Firefly rejected the submitted data (HTTP 422); the error carries per-field messages.</summary>
    public const string Validation = "firefly.validation";

    /// <summary>Firefly could not be reached or failed (connection error or HTTP 5xx).</summary>
    public const string Unavailable = "firefly.unavailable";

    /// <summary>Firefly did not answer in time.</summary>
    public const string Timeout = "firefly.timeout";

    /// <summary>Firefly answered with something the client does not expect (other 4xx, redirect).</summary>
    public const string Unexpected = "firefly.unexpected";

    /// <summary>Firefly answered successfully with a body the client cannot interpret.</summary>
    public const string InvalidResponse = "firefly.invalid_response";
}
