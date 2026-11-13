using Burndown.Common;
using Burndown.Entry;
using Burndown.Firefly;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Burndown.UnitTests.Entry;

internal static class EntryTestData
{
    public const int CashId = 16;
    public const int Checking = 1;
    public const int Savings = 2;

    public static readonly TimeZoneInfo Brussels = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    public static EntryOptions Options() =>
        new()
        {
            CashAccountId = CashId,
            MaxBackdatedMonths = 2,
            Categories = ["Leven", "Nodig", "Optioneel"],
            TargetTags = ["SharedDavid", "SharedTina", "Settled"],
            Budgets = [new BudgetOption { Id = 13, Name = "David" }, new BudgetOption { Id = 14, Name = "Tina" }],
        };

    public static IOptions<EntryOptions> OptionsAccessor() => Microsoft.Extensions.Options.Options.Create(Options());

    /// <summary>A clock fixed at the given UTC instant, in Europe/Brussels.</summary>
    public static (AppClock Clock, FakeTimeProvider Time) Clock(string utcInstant = "2026-05-15T10:00:00Z")
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse(utcInstant));
        return (new AppClock(time, Brussels), time);
    }

    public static EntryRowValidator Validator() => new(OptionsAccessor());

    public static SubmitRow Expense(
        string dateTime = "2026-05-15T09:30",
        string description = "Bakery",
        decimal? amount = 12.5m,
        int? accountId = Checking,
        string? category = "Leven",
        string? tag = "SharedDavid",
        int? budgetId = 13
    ) => new(RowTypes.Expense, dateTime, description, amount, accountId, null, null, category, tag, budgetId);

    public static SubmitRow Income(
        string dateTime = "2026-05-15T09:30",
        string description = "Salary",
        decimal? amount = 3000m,
        int? accountId = Checking
    ) => new(RowTypes.Income, dateTime, description, amount, accountId, null, null, null, null, null);

    public static SubmitRow Transfer(
        string dateTime = "2026-05-15T09:30",
        string description = "Savings",
        decimal? amount = 100m,
        int? from = Checking,
        int? to = Savings
    ) => new(RowTypes.Transfer, dateTime, description, amount, null, from, to, null, null, null);
}

/// <summary>
/// A Firefly client that never leaves the process: it records every created transaction and answers from scripts.
/// Entry tests use it so that nothing can reach a real Firefly (PLAN.md, "Write safety").
/// </summary>
internal sealed class RecordingFireflyClient : IFireflyClient
{
    private int _nextId = 100;

    public List<NewTransaction> Created { get; } = [];

    public List<string> Suggestions { get; } = [];

    public int AccountCalls { get; private set; }

    public Func<int, NewTransaction, Result<CreatedTransaction>?>? CreateScript { get; set; }

    public Func<Result<IReadOnlyList<FireflyAccount>>>? AccountsScript { get; set; }

    public Func<string, Result<IReadOnlyList<string>>>? SuggestScript { get; set; }

    public Task<Result<CreatedTransaction>> CreateTransactionAsync(
        NewTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var index = Created.Count;
        Created.Add(transaction);
        var scripted = CreateScript?.Invoke(index, transaction);
        return Task.FromResult(
            scripted ?? Result<CreatedTransaction>.Success(new CreatedTransaction(TransactionId.From(_nextId++)))
        );
    }

    public Task<Result<IReadOnlyList<FireflyAccount>>> GetAccountsAsync(
        AccountKind kind,
        CancellationToken cancellationToken
    )
    {
        AccountCalls++;
        return Task.FromResult(
            AccountsScript?.Invoke()
                ?? Result<IReadOnlyList<FireflyAccount>>.Success([
                    new FireflyAccount(AccountId.From(2), "savings", AccountKind.Asset, true),
                    new FireflyAccount(AccountId.From(1), "Checking", AccountKind.Asset, true),
                    new FireflyAccount(AccountId.From(3), "Closed", AccountKind.Asset, false),
                ])
        );
    }

    public Task<Result<IReadOnlyList<string>>> SuggestDescriptionsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken
    )
    {
        Suggestions.Add(query);
        return Task.FromResult(SuggestScript?.Invoke(query) ?? Result<IReadOnlyList<string>>.Success([query + " one"]));
    }

    public Task<Result<FireflyAbout>> GetAboutAsync(CancellationToken cancellationToken) => Unexpected<FireflyAbout>();

    public Task<Result<IReadOnlyList<string>>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        Unexpected<IReadOnlyList<string>>();

    public Task<Result<IReadOnlyList<string>>> GetTagsAsync(CancellationToken cancellationToken) =>
        Unexpected<IReadOnlyList<string>>();

    public Task<Result<IReadOnlyList<FireflyBudget>>> GetBudgetsAsync(CancellationToken cancellationToken) =>
        Unexpected<IReadOnlyList<FireflyBudget>>();

    public Task<Result<IReadOnlyList<FireflyTransaction>>> GetTransactionsAsync(
        DateOnly start,
        DateOnly end,
        TransactionType? type,
        CancellationToken cancellationToken
    ) => Unexpected<IReadOnlyList<FireflyTransaction>>();

    private static Task<Result<T>> Unexpected<T>() =>
        Task.FromResult(Result<T>.Failure(new Error("test.unexpected", "Not scripted.")));
}
