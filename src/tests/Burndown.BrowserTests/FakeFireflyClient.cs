using Burndown.Common;
using Burndown.Firefly;

namespace Burndown.BrowserTests;

/// <summary>A Firefly client with a fixed, synthetic transaction list. It never touches a real Firefly III.</summary>
internal sealed class FakeFireflyClient : IFireflyClient
{
    private readonly IReadOnlyList<FireflyTransaction> _transactions;

    public FakeFireflyClient(IReadOnlyList<FireflyTransaction> transactions)
    {
        _transactions = transactions;
    }

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

    public Task<Result<IReadOnlyList<FireflyAccount>>> GetAccountsAsync(
        AccountKind kind,
        CancellationToken cancellationToken
    ) => Unexpected<IReadOnlyList<FireflyAccount>>();

    public Task<Result<IReadOnlyList<string>>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        Unexpected<IReadOnlyList<string>>();

    public Task<Result<IReadOnlyList<string>>> GetTagsAsync(CancellationToken cancellationToken) =>
        Unexpected<IReadOnlyList<string>>();

    public Task<Result<IReadOnlyList<FireflyBudget>>> GetBudgetsAsync(CancellationToken cancellationToken) =>
        Unexpected<IReadOnlyList<FireflyBudget>>();

    public Task<Result<IReadOnlyList<string>>> SuggestDescriptionsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken
    ) => Unexpected<IReadOnlyList<string>>();

    public Task<Result<CreatedTransaction>> CreateTransactionAsync(
        NewTransaction transaction,
        CancellationToken cancellationToken
    ) => Unexpected<CreatedTransaction>();

    private static Task<Result<T>> Unexpected<T>() =>
        Task.FromResult(Result<T>.Failure(new Error("test.unexpected", "The overview must not call this.")));
}
