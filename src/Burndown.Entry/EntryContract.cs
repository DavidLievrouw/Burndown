namespace Burndown.Entry;

/// <summary>The row types of a submitted batch.</summary>
internal static class RowTypes
{
    /// <summary>A withdrawal from the chosen account to the cash account.</summary>
    public const string Expense = "expense";

    /// <summary>A deposit from the cash account into the chosen account.</summary>
    public const string Income = "income";

    /// <summary>A transfer between two accounts.</summary>
    public const string Transfer = "transfer";
}

/// <summary>The outcome of one row of a submitted batch.</summary>
internal static class RowStatuses
{
    /// <summary>The transaction was created in Firefly.</summary>
    public const string Created = "created";

    /// <summary>The app's own validation rejected the row. Nothing was sent to Firefly.</summary>
    public const string Invalid = "invalid";

    /// <summary>Firefly rejected the row (HTTP 422). The errors are mapped to the row's fields.</summary>
    public const string Rejected = "rejected";

    /// <summary>The row was sent but the call failed (Firefly unreachable, token refused, ...). It was not confirmed.</summary>
    public const string Failed = "failed";

    /// <summary>
    /// The row was sent but no usable answer came back (timeout, unreadable response): it may or may not exist in
    /// Firefly. It is never retried automatically.
    /// </summary>
    public const string Uncertain = "uncertain";

    /// <summary>The batch stopped before the row was sent.</summary>
    public const string NotAttempted = "notAttempted";
}

/// <summary>A batch of rows to create, in grid order.</summary>
/// <param name="Rows">The rows. Required, not empty and at most the batch limit.</param>
/// <param name="Previous">
/// The effective local date and times (<c>yyyy-MM-ddTHH:mm</c>) of rows that earlier requests of the same run already
/// created. Rows of this request on the same calendar dates are placed after them, so that a run sent row by row keeps
/// the entered order. Optional.
/// </param>
internal sealed record SubmitRequest(IReadOnlyList<SubmitRow>? Rows, IReadOnlyList<string>? Previous = null);

/// <summary>One row of a batch. Every field is optional on the wire so that the server can report what is missing.</summary>
/// <param name="Type">The row type, see <see cref="RowTypes"/>.</param>
/// <param name="DateTime">The local date and time in the configured zone, formatted as <c>yyyy-MM-ddTHH:mm</c>.</param>
/// <param name="Description">The description.</param>
/// <param name="Amount">The amount: positive, at most 2 decimals.</param>
/// <param name="AccountId">The chosen account of an expense (source) or an income (destination).</param>
/// <param name="FromAccountId">The source account of a transfer.</param>
/// <param name="ToAccountId">The destination account of a transfer.</param>
/// <param name="Category">The category of an expense. Ignored for other types.</param>
/// <param name="Tag">The target tag of an expense. Ignored for other types.</param>
/// <param name="BudgetId">The optional budget of an expense. Ignored for other types.</param>
internal sealed record SubmitRow(
    string? Type,
    string? DateTime,
    string? Description,
    decimal? Amount,
    int? AccountId,
    int? FromAccountId,
    int? ToAccountId,
    string? Category,
    string? Tag,
    int? BudgetId
);

/// <summary>The result of a submitted batch: one result per row, in the order of the request.</summary>
/// <param name="Results">The per-row results.</param>
/// <param name="Summary">The number of rows per status.</param>
/// <param name="Stopped">Why the batch stopped early, or <see langword="null"/> when it ran to the end.</param>
internal sealed record SubmitResponse(IReadOnlyList<RowResult> Results, SubmitSummary Summary, BatchStop? Stopped);

/// <summary>The result of one row.</summary>
/// <param name="Index">The zero-based position of the row in the request.</param>
/// <param name="Status">The outcome, see <see cref="RowStatuses"/>.</param>
/// <param name="Id">The Firefly transaction id when the row was created.</param>
/// <param name="DateTime">
/// The effective local date and time (<c>yyyy-MM-ddTHH:mm</c>) the row was sent with, when it was sent or is ready to be.
/// </param>
/// <param name="Bumped">
/// Whether the time was moved to keep the entry order or to keep a row dated today out of the future (see
/// <paramref name="DateTime"/>).
/// </param>
/// <param name="Code">The stable error code when the row did not succeed.</param>
/// <param name="Message">A user-readable message when the row did not succeed.</param>
/// <param name="Errors">Per-field messages keyed by the field names of <see cref="SubmitRow"/> (or <c>row</c>).</param>
internal sealed record RowResult(
    int Index,
    string Status,
    int? Id,
    string? DateTime,
    bool Bumped,
    string? Code,
    string? Message,
    IReadOnlyDictionary<string, string[]>? Errors
);

/// <summary>The number of rows per outcome.</summary>
/// <param name="Created">Rows created in Firefly.</param>
/// <param name="Invalid">Rows the app rejected.</param>
/// <param name="Rejected">Rows Firefly rejected.</param>
/// <param name="Failed">Rows whose call failed.</param>
/// <param name="Uncertain">Rows that may or may not exist in Firefly.</param>
/// <param name="NotAttempted">Rows that were not sent because the batch stopped.</param>
internal sealed record SubmitSummary(
    int Created,
    int Invalid,
    int Rejected,
    int Failed,
    int Uncertain,
    int NotAttempted
);

/// <summary>Why a batch stopped before its last row.</summary>
/// <param name="AtRow">The zero-based position of the row that stopped the batch.</param>
/// <param name="Code">The stable error code.</param>
/// <param name="Message">A user-readable message.</param>
internal sealed record BatchStop(int AtRow, string Code, string Message);

/// <summary>The data the entry grid needs before the first row: server time, allowed range and selectable values.</summary>
/// <param name="Now">The current instant in the configured zone, with its offset. Minute precision is up to the client.</param>
/// <param name="Today">The current date in the configured zone.</param>
/// <param name="TimeZone">The configured IANA time zone.</param>
/// <param name="MinDate">The earliest date a row may have.</param>
/// <param name="MaxDate">The latest date a row may have (tomorrow).</param>
/// <param name="MaxBatchSize">The largest batch the submit endpoint accepts.</param>
/// <param name="Accounts">The selectable accounts (active asset accounts), most recently used first.</param>
/// <param name="Categories">The selectable categories, most recently used first.</param>
/// <param name="TargetTags">The selectable target tags, most recently used first.</param>
/// <param name="Budgets">The selectable budgets, most recently used first.</param>
internal sealed record ReferenceResponse(
    DateTimeOffset Now,
    DateOnly Today,
    string TimeZone,
    DateOnly MinDate,
    DateOnly MaxDate,
    int MaxBatchSize,
    IReadOnlyList<AccountItem> Accounts,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> TargetTags,
    IReadOnlyList<BudgetItem> Budgets
);

/// <summary>A selectable account.</summary>
/// <param name="Id">The Firefly account id.</param>
/// <param name="Name">The account name, as stored in Firefly.</param>
internal sealed record AccountItem(int Id, string Name);

/// <summary>A selectable budget.</summary>
/// <param name="Id">The Firefly budget id.</param>
/// <param name="Name">The display name.</param>
internal sealed record BudgetItem(int Id, string Name);

/// <summary>Description suggestions in Firefly's order.</summary>
/// <param name="Descriptions">The distinct suggested descriptions.</param>
internal sealed record DescriptionSuggestions(IReadOnlyList<string> Descriptions);
