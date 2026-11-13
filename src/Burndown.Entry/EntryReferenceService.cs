using System.Globalization;
using Burndown.Common;
using Burndown.Firefly;
using Microsoft.Extensions.Options;

namespace Burndown.Entry;

/// <summary>Builds the reference data of the entry grid: server time, date range and ranked selectable values.</summary>
internal sealed class EntryReferenceService
{
    private static readonly StringComparer NameComparer = StringComparer.Create(
        CultureInfo.GetCultureInfo("nl-BE"),
        ignoreCase: true
    );

    private readonly IFireflyClient _firefly;
    private readonly AppClock _clock;
    private readonly EntryOptions _options;
    private readonly EntryUsageTracker _usage;
    private readonly AssetAccountCache _accounts;

    /// <summary>Initializes a new instance of the <see cref="EntryReferenceService"/> class.</summary>
    /// <param name="firefly">The Firefly client.</param>
    /// <param name="clock">The clock in the configured zone.</param>
    /// <param name="options">The Entry options.</param>
    /// <param name="usage">The last-use tracker that ranks the values.</param>
    /// <param name="accounts">The short-lived cache of the asset accounts.</param>
    public EntryReferenceService(
        IFireflyClient firefly,
        AppClock clock,
        IOptions<EntryOptions> options,
        EntryUsageTracker usage,
        AssetAccountCache accounts
    )
    {
        _firefly = firefly;
        _clock = clock;
        _options = options.Value;
        _usage = usage;
        _accounts = accounts;
    }

    /// <summary>Gets the reference data. The time and range are always current; only the account list is cached.</summary>
    /// <param name="refresh">Whether to reload the account list from Firefly instead of using the cache.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The reference data, or the Firefly failure.</returns>
    public async Task<Result<ReferenceResponse>> GetAsync(bool refresh, CancellationToken cancellationToken)
    {
        var loaded = await _accounts.GetAsync(
            ct => _firefly.GetAccountsAsync(AccountKind.Asset, ct),
            refresh,
            cancellationToken
        );
        if (!loaded.IsSuccess)
        {
            return Result<ReferenceResponse>.Failure(loaded.Error);
        }

        var accounts = loaded
            .Value.Where(a => a.Active)
            .OrderBy(a => a.Name, NameComparer)
            .ThenBy(a => a.Id.Value)
            .Select(a => new AccountItem(a.Id.Value, a.Name))
            .ToList();
        var budgets = _options.Budgets.ConvertAll(b => new BudgetItem(b.Id, b.Name!));

        var today = _clock.Today;
        return Result<ReferenceResponse>.Success(
            new ReferenceResponse(
                _clock.Now,
                today,
                _clock.Zone.Id,
                EntryDates.MinDate(today, _options),
                EntryDates.MaxDate(today),
                EntrySubmitService.MaxBatchSize,
                _usage.Rank(accounts),
                _usage.RankCategories(_options.Categories),
                _usage.RankTags(_options.TargetTags),
                _usage.Rank(budgets)
            )
        );
    }
}

/// <summary>The allowed date range of an entry row, in calendar dates of the configured zone.</summary>
internal static class EntryDates
{
    /// <summary>Gets the earliest allowed date.</summary>
    /// <param name="today">The current date in the configured zone.</param>
    /// <param name="options">The Entry options.</param>
    /// <returns>Today minus the configured number of months.</returns>
    public static DateOnly MinDate(DateOnly today, EntryOptions options) =>
        today.AddMonths(-options.MaxBackdatedMonths);

    /// <summary>Gets the latest allowed date.</summary>
    /// <param name="today">The current date in the configured zone.</param>
    /// <returns>Tomorrow.</returns>
    public static DateOnly MaxDate(DateOnly today) => today.AddDays(1);
}
