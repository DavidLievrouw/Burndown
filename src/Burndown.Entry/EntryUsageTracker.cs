namespace Burndown.Entry;

/// <summary>
/// Remembers, in process memory only (PLAN D15), which accounts, categories, tags and budgets were used by the
/// transactions that were created most recently. It is lost on restart and never persisted.
/// </summary>
internal sealed class EntryUsageTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<int, long> _accounts = [];
    private readonly Dictionary<string, long> _categories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, long> _budgets = [];
    private long _sequence;

    /// <summary>Records the values of one created transaction as the most recently used.</summary>
    /// <param name="accountIds">The accounts the user chose.</param>
    /// <param name="category">The category, or <see langword="null"/>.</param>
    /// <param name="tag">The target tag, or <see langword="null"/>.</param>
    /// <param name="budgetId">The budget, or <see langword="null"/>.</param>
    public void Record(IEnumerable<int> accountIds, string? category, string? tag, int? budgetId)
    {
        lock (_gate)
        {
            foreach (var accountId in accountIds)
            {
                _accounts[accountId] = ++_sequence;
            }

            if (category is not null)
            {
                _categories[category] = ++_sequence;
            }

            if (tag is not null)
            {
                _tags[tag] = ++_sequence;
            }

            if (budgetId is { } budget)
            {
                _budgets[budget] = ++_sequence;
            }
        }
    }

    /// <summary>Orders accounts: the used ones first (most recent first), then the rest in the given order.</summary>
    /// <param name="accounts">The accounts in their base order.</param>
    /// <returns>The ranked accounts.</returns>
    public IReadOnlyList<AccountItem> Rank(IReadOnlyList<AccountItem> accounts) =>
        Order(accounts, account => LastUse(_accounts, account.Id));

    /// <summary>Orders categories: the used ones first (most recent first), then the rest in the given order.</summary>
    /// <param name="categories">The categories in their base order.</param>
    /// <returns>The ranked categories.</returns>
    public IReadOnlyList<string> RankCategories(IReadOnlyList<string> categories) =>
        Order(categories, category => LastUse(_categories, category));

    /// <summary>Orders tags: the used ones first (most recent first), then the rest in the given order.</summary>
    /// <param name="tags">The tags in their base order.</param>
    /// <returns>The ranked tags.</returns>
    public IReadOnlyList<string> RankTags(IReadOnlyList<string> tags) => Order(tags, tag => LastUse(_tags, tag));

    /// <summary>Orders budgets: the used ones first (most recent first), then the rest in the given order.</summary>
    /// <param name="budgets">The budgets in their base order.</param>
    /// <returns>The ranked budgets.</returns>
    public IReadOnlyList<BudgetItem> Rank(IReadOnlyList<BudgetItem> budgets) =>
        Order(budgets, budget => LastUse(_budgets, budget.Id));

    private long LastUse<TKey>(Dictionary<TKey, long> uses, TKey key)
        where TKey : notnull
    {
        lock (_gate)
        {
            return uses.GetValueOrDefault(key);
        }
    }

    /// <summary>Orders used values by most recent use first. OrderBy is stable, so unused values keep their base order.</summary>
    /// <typeparam name="T">The type of the values.</typeparam>
    /// <param name="items">The values in their base order.</param>
    /// <param name="lastUse">Gets the last-use sequence number of a value, or 0 when it was never used.</param>
    private static List<T> Order<T>(IReadOnlyList<T> items, Func<T, long> lastUse) =>
        [.. items.OrderByDescending(lastUse)];
}
