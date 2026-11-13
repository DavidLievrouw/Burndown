using Burndown.Common;

namespace Burndown.Firefly;

/// <summary>Typed access to the Firefly III API v1. Expected failures are returned as errors, never thrown.</summary>
public interface IFireflyClient
{
    /// <summary>Gets information about the instance. A cheap call that proves connectivity and a valid token.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The instance information, or a failure.</returns>
    Task<Result<FireflyAbout>> GetAboutAsync(CancellationToken cancellationToken);

    /// <summary>Gets all accounts of a kind.</summary>
    /// <param name="kind">The kind of account.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>All accounts of that kind (every page), or a failure.</returns>
    Task<Result<IReadOnlyList<FireflyAccount>>> GetAccountsAsync(AccountKind kind, CancellationToken cancellationToken);

    /// <summary>Gets all category names.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The category names (every page), or a failure.</returns>
    Task<Result<IReadOnlyList<string>>> GetCategoriesAsync(CancellationToken cancellationToken);

    /// <summary>Gets all tag names.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The tag names (every page), or a failure.</returns>
    Task<Result<IReadOnlyList<string>>> GetTagsAsync(CancellationToken cancellationToken);

    /// <summary>Gets all budgets.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The budgets (every page), or a failure.</returns>
    Task<Result<IReadOnlyList<FireflyBudget>>> GetBudgetsAsync(CancellationToken cancellationToken);

    /// <summary>Suggests descriptions of earlier transactions that match a query.</summary>
    /// <param name="query">The text typed so far.</param>
    /// <param name="limit">The maximum number of suggestions to ask Firefly for.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The distinct descriptions in Firefly's order (ignoring case), or a failure.</returns>
    Task<Result<IReadOnlyList<string>>> SuggestDescriptionsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken
    );

    /// <summary>Gets the transaction splits in a date range.</summary>
    /// <param name="start">The first calendar date to include.</param>
    /// <param name="end">The last calendar date to include.</param>
    /// <param name="type">The transaction type to include, or <see langword="null"/> for all types.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>All splits (every page), flattened, or a failure.</returns>
    Task<Result<IReadOnlyList<FireflyTransaction>>> GetTransactionsAsync(
        DateOnly start,
        DateOnly end,
        TransactionType? type,
        CancellationToken cancellationToken
    );

    /// <summary>Creates a transaction, applying Firefly's rules and webhooks. Never retried automatically.</summary>
    /// <param name="transaction">The transaction to create.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The id of the new transaction, or a failure. A 422 carries per-field messages.</returns>
    Task<Result<CreatedTransaction>> CreateTransactionAsync(
        NewTransaction transaction,
        CancellationToken cancellationToken
    );
}
