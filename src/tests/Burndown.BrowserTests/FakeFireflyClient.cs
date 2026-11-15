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
    private Func<NewTransaction, Error?>? _createFailure;
    private Error? _readFailure;
    private int _transactionRequests;

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

    /// <summary>
    /// Decides per created transaction whether it fails: return an error to fail it, or <see langword="null"/> to let
    /// it succeed. Tests set it through <see cref="Script"/>, which resets it afterwards.
    /// </summary>
    public Func<NewTransaction, Error?>? CreateFailure
    {
        get
        {
            lock (_gate)
            {
                return _createFailure;
            }
        }
        set
        {
            lock (_gate)
            {
                _createFailure = value;
            }
        }
    }

    /// <summary>When set, every read (accounts, transactions, suggestions) fails with this error.</summary>
    public Error? ReadFailure
    {
        get
        {
            lock (_gate)
            {
                return _readFailure;
            }
        }
        set
        {
            lock (_gate)
            {
                _readFailure = value;
            }
        }
    }

    /// <summary>How many transaction list requests the overview made.</summary>
    public int TransactionRequests
    {
        get
        {
            lock (_gate)
            {
                return _transactionRequests;
            }
        }
    }

    /// <summary>Clears all scripted failures. Browser tests run one at a time, so a script never leaks.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _createFailure = null;
            _readFailure = null;
        }
    }

    /// <summary>Starts a scripted scenario; disposing the returned handle restores the always-succeeding default.</summary>
    /// <returns>The handle that resets the script.</returns>
    public IDisposable Script()
    {
        Reset();
        return new ScriptScope(this);
    }

    public Task<Result<CreatedTransaction>> CreateTransactionAsync(
        NewTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        lock (_gate)
        {
            var failure = _createFailure?.Invoke(transaction);
            if (failure is not null)
            {
                return Task.FromResult(Result<CreatedTransaction>.Failure(failure));
            }

            _created.Add(transaction);
            return Task.FromResult(
                Result<CreatedTransaction>.Success(new CreatedTransaction(TransactionId.From(1000 + _created.Count)))
            );
        }
    }

    public Task<Result<IReadOnlyList<FireflyAccount>>> GetAccountsAsync(
        AccountKind kind,
        CancellationToken cancellationToken
    )
    {
        if (ReadFailure is { } failure)
        {
            return Task.FromResult(Result<IReadOnlyList<FireflyAccount>>.Failure(failure));
        }

        if (kind != AccountKind.Asset)
        {
            return Task.FromResult(
                Result<IReadOnlyList<FireflyAccount>>.Failure(
                    new Error("test.unexpected", "Only asset accounts are served.")
                )
            );
        }

        return Task.FromResult(
            Result<IReadOnlyList<FireflyAccount>>.Success([
                new FireflyAccount(AccountId.From(1), "Checking", AccountKind.Asset, true),
                new FireflyAccount(AccountId.From(2), "Savings", AccountKind.Asset, true),
            ])
        );
    }

    public Task<Result<IReadOnlyList<string>>> SuggestDescriptionsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            ReadFailure is { } failure
                ? Result<IReadOnlyList<string>>.Failure(failure)
                : Result<IReadOnlyList<string>>.Success([
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
        lock (_gate)
        {
            _transactionRequests++;
        }

        if (ReadFailure is { } failure)
        {
            return Task.FromResult(Result<IReadOnlyList<FireflyTransaction>>.Failure(failure));
        }

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

    private sealed class ScriptScope : IDisposable
    {
        private readonly FakeFireflyClient _owner;

        public ScriptScope(FakeFireflyClient owner)
        {
            _owner = owner;
        }

        public void Dispose() => _owner.Reset();
    }
}
