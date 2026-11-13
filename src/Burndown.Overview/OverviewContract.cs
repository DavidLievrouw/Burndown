namespace Burndown.Overview;

/// <summary>The monthly overview as the BFF serves it. Money is rounded to 2 decimals, colours are intensity data.</summary>
/// <param name="Month">The viewed month, formatted as <c>yyyy-MM</c>.</param>
/// <param name="Today">The current date in the configured zone.</param>
/// <param name="MinimumMonth">The first month that can be viewed, formatted as <c>yyyy-MM</c>.</param>
/// <param name="MaximumMonth">The last month that can be viewed, formatted as <c>yyyy-MM</c>.</param>
/// <param name="CurrentMonth">The current month in the configured zone, formatted as <c>yyyy-MM</c>.</param>
/// <param name="Budget">How the month budget was derived.</param>
/// <param name="Days">One row per day of the month.</param>
/// <param name="Totals">The month totals.</param>
/// <param name="Chart">The burndown chart data.</param>
internal sealed record OverviewResponse(
    string Month,
    DateOnly Today,
    string MinimumMonth,
    string MaximumMonth,
    string CurrentMonth,
    BudgetSummary Budget,
    IReadOnlyList<DayRow> Days,
    MonthTotals Totals,
    BurndownChart Chart
);

/// <summary>How the month budget was derived.</summary>
/// <param name="Income">The income of the previous month.</param>
/// <param name="Installments">The monthly installments of the previous month.</param>
/// <param name="Computed">Income minus installments. It may be zero or negative.</param>
/// <param name="Effective">The budget the month is measured against: the override when present, else the computed one.</param>
/// <param name="Overridden">Whether the effective budget is an override.</param>
internal sealed record BudgetSummary(
    decimal Income,
    decimal Installments,
    decimal Computed,
    decimal Effective,
    bool Overridden
);

/// <summary>One day of the month.</summary>
/// <param name="Day">The day number, starting at 1.</param>
/// <param name="Date">The calendar date.</param>
/// <param name="IsToday">Whether the day is today in the configured zone.</param>
/// <param name="IsWeekend">Whether the day is a Saturday or Sunday.</param>
/// <param name="IsFuture">Whether the day is after today in the configured zone.</param>
/// <param name="Amount">The sum of the day's expenses.</param>
/// <param name="Description">The day's distinct descriptions, ordered and comma-separated. Empty when there are none.</param>
/// <param name="CumulativeBudget">The budget available up to and including this day.</param>
/// <param name="RunningTotal">The expenses up to and including this day.</param>
/// <param name="PerDay">The cumulative budget minus the running total: what is left (or overspent) at this day.</param>
/// <param name="PerDayTone">The colour of the per-day cell: <c>none</c>, <c>green</c> or <c>red</c>.</param>
/// <param name="PerDayAlpha">The intensity of the per-day colour, between 0 and 1.</param>
/// <param name="AmountAlpha">The intensity of the red colour of the amount cell, between 0 and 0.5.</param>
internal sealed record DayRow(
    int Day,
    DateOnly Date,
    bool IsToday,
    bool IsWeekend,
    bool IsFuture,
    decimal Amount,
    string Description,
    decimal CumulativeBudget,
    decimal RunningTotal,
    decimal PerDay,
    string PerDayTone,
    decimal PerDayAlpha,
    decimal AmountAlpha
);

/// <summary>The month totals.</summary>
/// <param name="Spent">The total expenses of the month.</param>
/// <param name="Remaining">The effective budget minus the total expenses.</param>
/// <param name="DailyBudget">The effective budget divided by the number of days in the month.</param>
internal sealed record MonthTotals(decimal Spent, decimal Remaining, decimal DailyBudget);

/// <summary>The burndown chart data.</summary>
/// <param name="Points">One point per day, from day 0 (the start of the month) to the last day.</param>
/// <param name="TodayDay">The day number of today when it lies in the viewed month; otherwise <see langword="null"/>.</param>
internal sealed record BurndownChart(IReadOnlyList<ChartPoint> Points, int? TodayDay);

/// <summary>One point of the burndown chart.</summary>
/// <param name="Day">The day number; 0 is the start of the month.</param>
/// <param name="Ideal">The remaining budget when it is spent evenly over the month.</param>
/// <param name="Actual">The actual remaining budget, or <see langword="null"/> for days after today.</param>
internal sealed record ChartPoint(int Day, decimal Ideal, decimal? Actual);

/// <summary>The calculated part of the overview, which does not depend on the allowed month range.</summary>
/// <param name="Budget">How the month budget was derived.</param>
/// <param name="Days">One row per day of the month.</param>
/// <param name="Totals">The month totals.</param>
/// <param name="Chart">The burndown chart data.</param>
internal sealed record OverviewCalculation(
    BudgetSummary Budget,
    IReadOnlyList<DayRow> Days,
    MonthTotals Totals,
    BurndownChart Chart
);

/// <summary>The values of <see cref="DayRow.PerDayTone"/>.</summary>
internal static class PerDayTones
{
    public const string None = "none";
    public const string Green = "green";
    public const string Red = "red";
}
