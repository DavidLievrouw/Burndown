using Burndown.Common;
using Burndown.Firefly;
using Microsoft.Extensions.Options;

namespace Burndown.Overview;

/// <summary>
/// Calculates the year overview: the results of the months of the viewed year that precede the viewed month, or of the
/// previous year when January is viewed. It reads the whole range with one deposit and one withdrawal query.
/// </summary>
internal sealed class YearOverviewService
{
    private readonly IFireflyClient _firefly;
    private readonly AppClock _clock;
    private readonly OverviewOptions _options;

    /// <summary>Initializes a new instance of the <see cref="YearOverviewService"/> class.</summary>
    /// <param name="firefly">The Firefly client.</param>
    /// <param name="clock">The clock in the configured zone.</param>
    /// <param name="options">The Overview options.</param>
    public YearOverviewService(IFireflyClient firefly, AppClock clock, IOptions<OverviewOptions> options)
    {
        _firefly = firefly;
        _clock = clock;
        _options = options.Value;
    }

    /// <summary>Gets the year overview for a viewed month.</summary>
    /// <param name="month">The viewed month as <c>yyyy-MM</c>, or <see langword="null"/> for the current month.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The year overview, or the validation or Firefly failure.</returns>
    public async Task<Result<YearOverviewResponse>> GetAsync(string? month, CancellationToken cancellationToken)
    {
        var currentMonth = _clock.CurrentMonth;
        _ = _options.TryGetMinimumMonth(out var minimumMonth);
        var maximumMonth = currentMonth.AddMonths(1);

        var viewedMonth = currentMonth;
        string? problem = null;
        if (month is not null && !OverviewService.TryParseMonth(month, out viewedMonth))
        {
            problem = "The month must be formatted as yyyy-MM.";
        }
        else if (viewedMonth < minimumMonth || viewedMonth > maximumMonth)
        {
            problem =
                $"The month must be between {OverviewService.FormatMonth(minimumMonth)} and {OverviewService.FormatMonth(maximumMonth)}.";
        }

        if (problem is not null)
        {
            return Result<YearOverviewResponse>.Failure(
                new Error(
                    ErrorCodes.Validation,
                    "The year overview request is not valid.",
                    new Dictionary<string, string[]> { ["month"] = [problem] }
                )
            );
        }

        var year = viewedMonth.Month == 1 ? viewedMonth.Year - 1 : viewedMonth.Year;
        var firstMonth = new DateOnly(year, 1, 1);
        if (firstMonth < minimumMonth)
        {
            firstMonth = minimumMonth;
        }

        var months = new List<DateOnly>();
        for (
            var candidate = firstMonth;
            candidate < viewedMonth && candidate.Year == year;
            candidate = candidate.AddMonths(1)
        )
        {
            months.Add(candidate);
        }

        if (months.Count == 0)
        {
            return Result<YearOverviewResponse>.Success(
                new YearOverviewResponse(OverviewService.FormatMonth(viewedMonth), year, 0m, [])
            );
        }

        var lastMonth = months[^1];
        var depositsTask = _firefly.GetTransactionsAsync(
            firstMonth.AddMonths(-1),
            lastMonth.AddDays(-1),
            TransactionType.Deposit,
            cancellationToken
        );
        var withdrawalsTask = _firefly.GetTransactionsAsync(
            firstMonth.AddMonths(-1),
            lastMonth.AddMonths(1).AddDays(-1),
            TransactionType.Withdrawal,
            cancellationToken
        );
        await Task.WhenAll(depositsTask, withdrawalsTask);

        var deposits = await depositsTask;
        if (!deposits.IsSuccess)
        {
            return Result<YearOverviewResponse>.Failure(deposits.Error);
        }

        var withdrawals = await withdrawalsTask;
        if (!withdrawals.IsSuccess)
        {
            return Result<YearOverviewResponse>.Failure(withdrawals.Error);
        }

        IReadOnlyList<FireflyTransaction> transactions = [.. deposits.Value, .. withdrawals.Value];
        var today = _clock.Today;
        var results = months.ConvertAll(m => new MonthResult(
            OverviewService.FormatMonth(m),
            OverviewCalculator.Calculate(transactions, m, today, _options, null).Totals.Remaining
        ));

        return Result<YearOverviewResponse>.Success(
            new YearOverviewResponse(
                OverviewService.FormatMonth(viewedMonth),
                year,
                results.Sum(r => r.Result),
                results
            )
        );
    }
}
