using Burndown.Common;
using Burndown.Firefly;

namespace Burndown.BrowserTests;

/// <summary>
/// An in-memory Firefly client with a fixed, synthetic transaction list. It records created transactions in memory and
/// never touches a real Firefly III.
/// </summary>
internal sealed class FakeFireflyClient : IFireflyClient
{
    private static readonly string[] KnownDescriptions = ["Bakery Delhaize", "Bakery Local", "Butcher", "Pharmacy"];

    private readonly IReadOnlyList<FireflyTransaction> _transactions;
    private readonly List<NewTransaction> _created = [];
    private readonly Lock _gate = new();

    public FakeFireflyClient(IReadOnlyList<FireflyTransaction> transactions)
    {
        _transactions = transactions;
    }

    /// <summary>The transactions the entry grid created, in the order they arrived.</summary>
    public IReadOnlyList<NewTransaction> Created
    {
        get
        {
            lock (_gate)
            {
                return [.. _created];
            }
        }
    }

    public Task<Result<CreatedTransaction>> CreateTransactionAsync(
        NewTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        lock (_gate)
        {
            _created.Add(transaction);
            return Task.FromResult(
                Result<CreatedTransaction>.Success(new CreatedTransaction(TransactionId.From(1000 + _created.Count)))
            );
        }
    }

    public Task<Result<IReadOnlyList<FireflyAccount>>> GetAccountsAsync(
        AccountKind kind,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            kind == AccountKind.Asset
                ? Result<IReadOnlyList<FireflyAccount>>.Success([
                    new FireflyAccount(AccountId.From(1), "Checking", AccountKind.Asset, true),
                    new FireflyAccount(AccountId.From(2), "Savings", AccountKind.Asset, true),
                ])
                : Result<IReadOnlyList<FireflyAccount>>.Failure(
                    new Error("test.unexpected", "Only asset accounts are served.")
                )
        );

    public Task<Result<IReadOnlyList<string>>> SuggestDescriptionsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            Result<IReadOnlyList<string>>.Success([
                .. KnownDescriptions.Where(d => d.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(limit),
            ])
        );

    public Task<Result<IReadOnlyList<FireflyTransaction>>> GetTransactionsAsync(
        DateOnly start,
        DateOnly end,
        TransactionType? type,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<FireflyTransaction> matches =
        [
            .. _transactions.Where(t =>
            {
                var date = DateOnly.FromDateTime(t.Date.DateTime);
                return date >= start && date <= end && (type is null || t.Type == type);
            }),
        ];
        return Task.FromResult(Result<IReadOnlyList<FireflyTransaction>>.Success(matches));
    }

    public Task<Result<FireflyAbout>> GetAboutAsync(CancellationToken cancellationToken) => Unexpected<FireflyAbout>();

    public Task<Result<IReadOnlyList<string>>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        Unexpected<IReadOnlyList<string>>();

    public Task<Result<IReadOnlyList<string>>> GetTagsAsync(CancellationToken cancellationToken) =>
        Unexpected<IReadOnlyList<string>>();

    public Task<Result<IReadOnlyList<FireflyBudget>>> GetBudgetsAsync(CancellationToken cancellationToken) =>
        Unexpected<IReadOnlyList<FireflyBudget>>();

    private static Task<Result<T>> Unexpected<T>() =>
        Task.FromResult(Result<T>.Failure(new Error("test.unexpected", "The browser tests must not call this.")));
}
