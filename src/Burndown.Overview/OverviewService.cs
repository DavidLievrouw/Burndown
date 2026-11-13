using System.Globalization;
using Burndown.Common;
using Burndown.Firefly;
using Microsoft.Extensions.Options;

namespace Burndown.Overview;

/// <summary>Validates an overview request, reads what is needed from Firefly and calculates the overview.</summary>
internal sealed class OverviewService
{
    /// <summary>The largest month budget override that is accepted.</summary>
    public const decimal MaximumOverride = 999_999_999.99m;

    private readonly IFireflyClient _firefly;
    private readonly AppClock _clock;
    private readonly OverviewOptions _options;

    /// <summary>Initializes a new instance of the <see cref="OverviewService"/> class.</summary>
    /// <param name="firefly">The Firefly client.</param>
    /// <param name="clock">The clock in the configured zone.</param>
    /// <param name="options">The Overview options.</param>
    public OverviewService(IFireflyClient firefly, AppClock clock, IOptions<OverviewOptions> options)
    {
        _firefly = firefly;
        _clock = clock;
        _options = options.Value;
    }

    /// <summary>Gets the overview of a month.</summary>
    /// <param name="month">The month as <c>yyyy-MM</c>, or <see langword="null"/> for the current month.</param>
    /// <param name="budgetOverride">
    /// The month budget that replaces the computed one, as an invariant-culture decimal, or <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The overview, or the validation or Firefly failure.</returns>
    public async Task<Result<OverviewResponse>> GetAsync(
        string? month,
        string? budgetOverride,
        CancellationToken cancellationToken
    )
    {
        var today = _clock.Today;
        var currentMonth = _clock.CurrentMonth;
        _ = _options.TryGetMinimumMonth(out var minimumMonth);
        var maximumMonth = currentMonth.AddMonths(1);

        var fields = new Dictionary<string, string[]>();
        var viewedMonth = currentMonth;
        if (month is not null && !TryParseMonth(month, out viewedMonth))
        {
            fields["month"] = ["The month must be formatted as yyyy-MM."];
        }
        else if (viewedMonth < minimumMonth || viewedMonth > maximumMonth)
        {
            fields["month"] =
            [
                $"The month must be between {FormatMonth(minimumMonth)} and {FormatMonth(maximumMonth)}.",
            ];
        }

        decimal? parsedOverride = null;
        if (budgetOverride is not null)
        {
            if (
                !decimal.TryParse(
                    budgetOverride,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var value
                ) || value is < 0 or > MaximumOverride
            )
            {
                fields["budgetOverride"] = ["The budget override must be a number between 0 and 999999999.99."];
            }
            else
            {
                parsedOverride = value;
            }
        }

        if (fields.Count > 0)
        {
            return Result<OverviewResponse>.Failure(
                new Error(ErrorCodes.Validation, "The overview request is not valid.", fields)
            );
        }

        var previousMonth = viewedMonth.AddMonths(-1);
        var lastDay = viewedMonth.AddMonths(1).AddDays(-1);
        var depositsTask = _firefly.GetTransactionsAsync(
            previousMonth,
            viewedMonth.AddDays(-1),
            TransactionType.Deposit,
            cancellationToken
        );
        var withdrawalsTask = _firefly.GetTransactionsAsync(
            previousMonth,
            lastDay,
            TransactionType.Withdrawal,
            cancellationToken
        );
        await Task.WhenAll(depositsTask, withdrawalsTask);

        var deposits = await depositsTask;
        if (!deposits.IsSuccess)
        {
            return Result<OverviewResponse>.Failure(deposits.Error);
        }

        var withdrawals = await withdrawalsTask;
        if (!withdrawals.IsSuccess)
        {
            return Result<OverviewResponse>.Failure(withdrawals.Error);
        }

        var calculation = OverviewCalculator.Calculate(
            [.. deposits.Value, .. withdrawals.Value],
            viewedMonth,
            today,
            _options,
            parsedOverride
        );

        return Result<OverviewResponse>.Success(
            new OverviewResponse(
                FormatMonth(viewedMonth),
                today,
                FormatMonth(minimumMonth),
                FormatMonth(maximumMonth),
                FormatMonth(currentMonth),
                calculation.Budget,
                calculation.Days,
                calculation.Totals,
                calculation.Chart
            )
        );
    }

    /// <summary>Parses a <c>yyyy-MM</c> month.</summary>
    /// <param name="value">The text to parse.</param>
    /// <param name="firstDay">The first day of the month when parsing succeeds.</param>
    /// <returns>Whether the text is a valid month.</returns>
    internal static bool TryParseMonth(string value, out DateOnly firstDay)
    {
        firstDay = default;
        return value.Length == 7
            && DateOnly.TryParseExact(
                value + "-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out firstDay
            );
    }

    /// <summary>Formats the month of a date as <c>yyyy-MM</c>.</summary>
    /// <param name="month">A date in the month.</param>
    /// <returns>The formatted month.</returns>
    internal static string FormatMonth(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
