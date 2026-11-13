using Burndown.Firefly;

namespace Burndown.Overview;

/// <summary>
/// The one place that derives the month budget, the daily series, the colour intensities and the chart. It is pure:
/// it works on the transactions it is given and never reads a clock.
/// </summary>
internal static class OverviewCalculator
{
    /// <summary>Calculates the overview of one month.</summary>
    /// <param name="transactions">
    /// The deposits of the previous month and the withdrawals of the previous and the viewed month. Anything else, and
    /// anything outside those months, is ignored.
    /// </param>
    /// <param name="month">The first day of the viewed month.</param>
    /// <param name="today">The current date in the configured zone.</param>
    /// <param name="options">The Overview options.</param>
    /// <param name="budgetOverride">The month budget that replaces the computed one, or <see langword="null"/>.</param>
    /// <returns>The calculated overview.</returns>
    public static OverviewCalculation Calculate(
        IReadOnlyList<FireflyTransaction> transactions,
        DateOnly month,
        DateOnly today,
        OverviewOptions options,
        decimal? budgetOverride
    )
    {
        var installmentTag = options.MonthlyInstallmentTag ?? string.Empty;
        var excludedDestinationType = options.ExcludedDestinationAccountType ?? string.Empty;
        var previousMonth = month.AddMonths(-1);
        var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);

        var income = 0m;
        var installments = 0m;
        var expenses = new List<(int Day, decimal Amount, string Description)>();
        foreach (var transaction in transactions)
        {
            var date = DateOnly.FromDateTime(transaction.Date.DateTime);
            var inPreviousMonth = date.Year == previousMonth.Year && date.Month == previousMonth.Month;
            var inMonth = date.Year == month.Year && date.Month == month.Month;
            var isInstallment = transaction.Tags.Contains(installmentTag);

            if (transaction.Type == TransactionType.Deposit && inPreviousMonth)
            {
                income += transaction.Amount;
            }
            else if (transaction.Type == TransactionType.Withdrawal)
            {
                if (inPreviousMonth && isInstallment)
                {
                    installments += transaction.Amount;
                }
                else if (inMonth && !isInstallment && transaction.DestinationType != excludedDestinationType)
                {
                    expenses.Add((date.Day, transaction.Amount, transaction.Description));
                }
            }
        }

        var computedBudget = income - installments;
        var monthBudget = budgetOverride ?? computedBudget;
        var dailyBudget = monthBudget / daysInMonth;

        var amounts = new decimal[daysInMonth + 1];
        var descriptions = new string[daysInMonth + 1];
        for (var day = 1; day <= daysInMonth; day++)
        {
            var ofDay = expenses.Where(e => e.Day == day).ToList();
            amounts[day] = ofDay.Sum(e => e.Amount);
            descriptions[day] = string.Join(
                ", ",
                ofDay
                    .Where(e => !string.IsNullOrWhiteSpace(e.Description))
                    .Select(e => e.Description)
                    .DistinctBy(d => d, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(d => d, StringComparer.InvariantCultureIgnoreCase)
            );
        }

        var cumulative = new decimal[daysInMonth + 1];
        var running = new decimal[daysInMonth + 1];
        var perDay = new decimal[daysInMonth + 1];
        for (var day = 1; day <= daysInMonth; day++)
        {
            cumulative[day] = dailyBudget * day;
            running[day] = running[day - 1] + amounts[day];
            perDay[day] = cumulative[day] - running[day];
        }

        var elapsedDays = Enumerable.Range(1, daysInMonth).Where(day => month.AddDays(day - 1) <= today).ToList();
        var minPerDay = elapsedDays.Count > 0 ? elapsedDays.Min(day => perDay[day]) : 0m;
        var maxPerDay = elapsedDays.Count > 0 ? elapsedDays.Max(day => perDay[day]) : 0m;
        var amountRange = Math.Max(1m, Math.Abs(amounts.Max()));

        var rows = new List<DayRow>(daysInMonth);
        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = month.AddDays(day - 1);
            var isFuture = date > today;
            var (tone, alpha) = isFuture ? (PerDayTones.None, 0m) : PerDayColor(perDay[day], minPerDay, maxPerDay);

            rows.Add(
                new DayRow(
                    day,
                    date,
                    date == today,
                    date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                    isFuture,
                    Round2(amounts[day]),
                    descriptions[day],
                    Round2(cumulative[day]),
                    Round2(running[day]),
                    Round2(perDay[day]),
                    tone,
                    Round2(alpha),
                    Round2(Math.Min(1m, Math.Abs(amounts[day]) / amountRange) / 2)
                )
            );
        }

        var points = new List<ChartPoint>(daysInMonth + 1)
        {
            new(0, Round2(monthBudget), month <= today ? Round2(monthBudget) : null),
        };
        for (var day = 1; day <= daysInMonth; day++)
        {
            points.Add(
                new ChartPoint(
                    day,
                    Round2(monthBudget - cumulative[day]),
                    rows[day - 1].IsFuture ? null : Round2(monthBudget - running[day])
                )
            );
        }

        var inViewedMonth = today.Year == month.Year && today.Month == month.Month;
        var spent = running[daysInMonth];

        return new OverviewCalculation(
            new BudgetSummary(
                Round2(income),
                Round2(installments),
                Round2(computedBudget),
                Round2(monthBudget),
                budgetOverride is not null
            ),
            rows,
            new MonthTotals(Round2(spent), Round2(monthBudget - spent), Round2(dailyBudget)),
            new BurndownChart(points, inViewedMonth ? today.Day : null)
        );
    }

    private static (string Tone, decimal Alpha) PerDayColor(decimal perDay, decimal minPerDay, decimal maxPerDay)
    {
        var range = perDay < 0 ? Math.Abs(minPerDay) : maxPerDay;
        if (range == 0)
        {
            return (perDay < 0 ? PerDayTones.Red : PerDayTones.Green, 0m);
        }

        return perDay >= 0
            ? (PerDayTones.Green, Math.Min(1m, perDay / range))
            : (PerDayTones.Red, Math.Min(1m, Math.Abs(perDay) / range) / 2);
    }

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
