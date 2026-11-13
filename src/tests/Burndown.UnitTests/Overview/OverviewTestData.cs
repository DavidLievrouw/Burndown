using Burndown.Common;
using Burndown.Firefly;
using Burndown.Overview;

namespace Burndown.UnitTests.Overview;

internal static class OverviewTestData
{
    public static OverviewOptions DefaultOptions() =>
        new()
        {
            MinimumMonth = "2023-12",
            MonthlyInstallmentTag = "MonthlyInstallment",
            ExcludedDestinationAccountType = "Expense account",
        };

    public static FireflyTransaction Tx(
        TransactionType type,
        int year,
        int month,
        int day,
        decimal amount,
        string description = "x",
        string[]? tags = null,
        string destinationType = "Cash account",
        TimeSpan? offset = null
    ) =>
        new(
            TransactionId.From(1),
            1,
            type,
            new DateTimeOffset(year, month, day, 12, 0, 0, offset ?? TimeSpan.FromHours(1)),
            amount,
            description,
            AccountId.From(1),
            "Asset account",
            AccountId.From(2),
            destinationType,
            null,
            null,
            tags ?? []
        );

    public static FireflyTransaction Withdrawal(
        int year,
        int month,
        int day,
        decimal amount,
        string description = "x",
        string[]? tags = null,
        string destinationType = "Cash account"
    ) => Tx(TransactionType.Withdrawal, year, month, day, amount, description, tags, destinationType);

    public static FireflyTransaction Deposit(int year, int month, int day, decimal amount) =>
        Tx(TransactionType.Deposit, year, month, day, amount, "Salary", null, "Asset account");
}

/// <summary>A Firefly client whose transaction list is scripted. It records every list call.</summary>
internal sealed class FakeFireflyClient : IFireflyClient
{
    private readonly Func<
        DateOnly,
        DateOnly,
        TransactionType?,
        Result<IReadOnlyList<FireflyTransaction>>
    > _transactions;

    public FakeFireflyClient(
        Func<DateOnly, DateOnly, TransactionType?, Result<IReadOnlyList<FireflyTransaction>>> transactions
    )
    {
        _transactions = transactions;
    }

    public List<(DateOnly Start, DateOnly End, TransactionType? Type)> Calls { get; } = [];

    public int OtherCalls { get; private set; }

    public static FakeFireflyClient Returning(params FireflyTransaction[] transactions) =>
        new(
            (start, end, type) =>
                Result<IReadOnlyList<FireflyTransaction>>.Success([
                    .. transactions.Where(t =>
                    {
                        var date = DateOnly.FromDateTime(t.Date.DateTime);
                        return date >= start && date <= end && (type is null || t.Type == type);
                    }),
                ])
        );

    public Task<Result<IReadOnlyList<FireflyTransaction>>> GetTransactionsAsync(
        DateOnly start,
        DateOnly end,
        TransactionType? type,
        CancellationToken cancellationToken
    )
    {
        lock (Calls)
        {
            Calls.Add((start, end, type));
        }

        return Task.FromResult(_transactions(start, end, type));
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

    private Task<Result<T>> Unexpected<T>()
    {
        OtherCalls++;
        return Task.FromResult(Result<T>.Failure(new Error("test.unexpected", "The overview must not call this.")));
    }
}
