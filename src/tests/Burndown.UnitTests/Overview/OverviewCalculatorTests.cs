using System.Globalization;
using Burndown.Firefly;
using Burndown.Overview;
using Shouldly;
using static Burndown.UnitTests.Overview.OverviewTestData;

namespace Burndown.UnitTests.Overview;

public class OverviewCalculatorTests
{
    private static readonly DateOnly Feb2024 = new(2024, 2, 1);
    private static readonly DateOnly FarFuture = new(2030, 1, 1);

    private static OverviewCalculation Calculate(
        IEnumerable<FireflyTransaction> transactions,
        DateOnly month,
        DateOnly today,
        decimal? budgetOverride = null
    ) => OverviewCalculator.Calculate([.. transactions], month, today, DefaultOptions(), budgetOverride);

    private static FireflyTransaction[] BudgetOf(int year, int month, decimal income, decimal installments = 0m) =>
        [
            Deposit(year, month, 1, income),
            Withdrawal(year, month, 2, installments, "Loan", ["MonthlyInstallment"], "Expense account"),
        ];

    [Theory]
    [InlineData(2023, 2, 28)]
    [InlineData(2024, 2, 29)]
    [InlineData(2024, 4, 30)]
    [InlineData(2024, 1, 31)]
    public void Has_one_row_per_day_and_a_ideal_line_that_burns_down_to_zero(int year, int month, int days)
    {
        var first = new DateOnly(year, month, 1);
        var previous = first.AddMonths(-1);

        var result = Calculate(BudgetOf(previous.Year, previous.Month, 3100m), first, FarFuture);

        result.Days.Count.ShouldBe(days);
        result.Days[^1].Date.ShouldBe(first.AddMonths(1).AddDays(-1));
        result.Days[^1].CumulativeBudget.ShouldBe(3100m);
        result.Chart.Points.Count.ShouldBe(days + 1);
        result.Chart.Points[0].Day.ShouldBe(0);
        result.Chart.Points[0].Ideal.ShouldBe(3100m);
        result.Chart.Points[^1].Ideal.ShouldBe(0m);
        result.Chart.Points[^1].Actual.ShouldBe(3100m);
    }

    [Fact]
    public void Month_budget_is_income_minus_installments_of_the_previous_month()
    {
        var result = Calculate(
            [
                Deposit(2024, 1, 5, 2000m),
                Deposit(2024, 1, 25, 500.50m),
                Deposit(2023, 12, 31, 999m),
                Deposit(2024, 2, 1, 999m),
                Withdrawal(2024, 1, 10, 300m, "Mortgage", ["MonthlyInstallment"], "Expense account"),
                Withdrawal(2024, 1, 11, 25m, "Insurance", ["Other", "MonthlyInstallment"]),
                Withdrawal(2024, 1, 12, 80m, "Groceries"),
                Withdrawal(2024, 2, 3, 40m, "Mortgage", ["MonthlyInstallment"]),
            ],
            Feb2024,
            FarFuture
        );

        result.Budget.Income.ShouldBe(2500.50m);
        result.Budget.Installments.ShouldBe(325m);
        result.Budget.Computed.ShouldBe(2175.50m);
        result.Budget.Effective.ShouldBe(2175.50m);
        result.Budget.Overridden.ShouldBeFalse();
        result.Totals.Spent.ShouldBe(0m);
        result.Totals.Remaining.ShouldBe(2175.50m);
    }

    [Fact]
    public void Zero_income_gives_a_zero_budget_without_dividing_by_zero()
    {
        var result = Calculate([Withdrawal(2024, 2, 2, 10m, "Snack")], Feb2024, FarFuture);

        result.Budget.Effective.ShouldBe(0m);
        result.Totals.DailyBudget.ShouldBe(0m);
        result.Days[0].PerDay.ShouldBe(0m);
        result.Days[0].PerDayAlpha.ShouldBe(0m);
        result.Days[1].PerDay.ShouldBe(-10m);
        result.Days[1].PerDayTone.ShouldBe(PerDayTones.Red);
        result.Days[1].PerDayAlpha.ShouldBe(0.5m);
        result.Totals.Remaining.ShouldBe(-10m);
    }

    [Fact]
    public void A_negative_month_budget_is_kept()
    {
        var result = Calculate(BudgetOf(2024, 1, 100m, 400m), Feb2024, FarFuture);

        result.Budget.Computed.ShouldBe(-300m);
        result.Totals.DailyBudget.ShouldBe(-10.34m);
        result.Days[0].PerDay.ShouldBe(-10.34m);
        result.Days[^1].PerDay.ShouldBe(-300m);
        result.Chart.Points[0].Ideal.ShouldBe(-300m);
        result.Chart.Points[^1].Ideal.ShouldBe(0m);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1450.25")]
    public void An_override_replaces_the_computed_budget(string value)
    {
        var budgetOverride = decimal.Parse(value, CultureInfo.InvariantCulture);

        var result = Calculate(BudgetOf(2024, 1, 3000m, 200m), Feb2024, FarFuture, budgetOverride);

        result.Budget.Computed.ShouldBe(2800m);
        result.Budget.Effective.ShouldBe(budgetOverride);
        result.Budget.Overridden.ShouldBeTrue();
        result.Days[^1].CumulativeBudget.ShouldBe(budgetOverride);
        result.Chart.Points[0].Ideal.ShouldBe(budgetOverride);
    }

    [Fact]
    public void Only_withdrawals_of_the_month_that_are_not_installments_or_expense_account_payments_are_expenses()
    {
        var result = Calculate(
            [
                Withdrawal(2024, 2, 4, 10m, "Counted"),
                Withdrawal(2024, 2, 4, 5m, "Installment", ["MonthlyInstallment"]),
                Withdrawal(2024, 2, 4, 7m, "Payee", null, "Expense account"),
                Withdrawal(2024, 1, 31, 99m, "Previous month"),
                Withdrawal(2024, 3, 1, 99m, "Next month"),
                Tx(TransactionType.Transfer, 2024, 2, 4, 300m, "Transfer", null, "Asset account"),
                Deposit(2024, 2, 4, 300m),
                Withdrawal(2024, 2, 4, 3m, "Tagged", ["Other"]),
            ],
            Feb2024,
            FarFuture
        );

        result.Days[3].Amount.ShouldBe(13m);
        result.Days[3].Description.ShouldBe("Counted, Tagged");
        result.Totals.Spent.ShouldBe(13m);
    }

    [Fact]
    public void Descriptions_are_distinct_ignoring_case_ordered_and_skip_empty_ones()
    {
        var result = Calculate(
            [
                Withdrawal(2024, 2, 7, 1m, "zebra"),
                Withdrawal(2024, 2, 7, 1m, "Bakery"),
                Withdrawal(2024, 2, 7, 1m, "BAKERY"),
                Withdrawal(2024, 2, 7, 1m, ""),
                Withdrawal(2024, 2, 7, 1m, "apple"),
            ],
            Feb2024,
            FarFuture
        );

        result.Days[6].Description.ShouldBe("apple, Bakery, zebra");
        result.Days[6].Amount.ShouldBe(5m);
        result.Days[5].Description.ShouldBe("");
    }

    [Fact]
    public void A_split_belongs_to_the_date_part_it_has_in_its_own_offset()
    {
        // 00:30 on 1 February at UTC+2 is still 31 January in UTC.
        var late = Tx(TransactionType.Withdrawal, 2024, 2, 1, 5m, "Early", offset: TimeSpan.FromHours(2)) with
        {
            Date = new DateTimeOffset(2024, 2, 1, 0, 30, 0, TimeSpan.FromHours(2)),
        };

        var result = Calculate([late], Feb2024, FarFuture);

        result.Days[0].Amount.ShouldBe(5m);
    }

    [Fact]
    public void Marks_weekends()
    {
        var result = Calculate([], Feb2024, FarFuture);

        result.Days.Where(d => d.IsWeekend).Select(d => d.Day).ShouldBe([3, 4, 10, 11, 17, 18, 24, 25]);
    }

    [Fact]
    public void In_the_current_month_future_days_have_no_colour_and_no_actual_remaining()
    {
        var result = Calculate(
            [.. BudgetOf(2024, 1, 2900m), Withdrawal(2024, 2, 2, 50m, "Shop")],
            Feb2024,
            new DateOnly(2024, 2, 3)
        );

        result.Chart.TodayDay.ShouldBe(3);
        result.Days.Where(d => d.IsToday).Select(d => d.Day).ShouldBe([3]);
        result.Days.Count(d => d.IsFuture).ShouldBe(26);
        result.Days.Where(d => d.IsFuture).ShouldAllBe(d => d.PerDayTone == PerDayTones.None && d.PerDayAlpha == 0m);
        result.Chart.Points[0].Actual.ShouldBe(2900m);
        result.Chart.Points[3].Actual.ShouldBe(2850m);
        result.Chart.Points[4].Actual.ShouldBeNull();
        result.Chart.Points[^1].Actual.ShouldBeNull();
    }

    [Fact]
    public void Per_day_colour_is_scaled_by_the_largest_value_up_to_today_only()
    {
        // daily budget 100: per day is 100, 200, 300 up to today; later days (400, ...) do not influence the range.
        var result = Calculate(BudgetOf(2024, 1, 2900m), Feb2024, new DateOnly(2024, 2, 3));

        result.Days.Take(3).Select(d => d.PerDayTone).ShouldAllBe(t => t == PerDayTones.Green);
        result.Days.Take(3).Select(d => d.PerDayAlpha).ShouldBe([0.33m, 0.67m, 1m]);
    }

    [Fact]
    public void Colour_intensities_are_rounded_half_away_from_zero()
    {
        // Amount 1 against a range of 100 is 0.01 / 2 = 0.005, which rounds up to 0.01.
        var result = Calculate(
            [Withdrawal(2024, 2, 5, 1m, "Small"), Withdrawal(2024, 2, 6, 100m, "Big")],
            Feb2024,
            FarFuture
        );

        result.Days[4].AmountAlpha.ShouldBe(0.01m);
        result.Days[5].AmountAlpha.ShouldBe(0.5m);
        result.Days[0].AmountAlpha.ShouldBe(0m);
    }

    [Fact]
    public void Amount_colour_range_is_at_least_one()
    {
        var result = Calculate([Withdrawal(2024, 2, 5, 0.5m, "Tiny")], Feb2024, FarFuture);

        result.Days[4].AmountAlpha.ShouldBe(0.25m);
    }

    [Fact]
    public void A_past_month_has_an_actual_remaining_for_every_day()
    {
        var result = Calculate(BudgetOf(2024, 1, 2900m), Feb2024, new DateOnly(2024, 4, 1));

        result.Chart.TodayDay.ShouldBeNull();
        result.Chart.Points.ShouldAllBe(p => p.Actual != null);
        result.Days.ShouldAllBe(d => !d.IsFuture && !d.IsToday);
    }

    [Fact]
    public void A_future_month_has_no_actual_remaining_and_no_colours()
    {
        var result = Calculate(BudgetOf(2024, 1, 2900m), Feb2024, new DateOnly(2024, 1, 31));

        result.Chart.TodayDay.ShouldBeNull();
        result.Chart.Points.ShouldAllBe(p => p.Actual == null);
        result.Days.ShouldAllBe(d => d.IsFuture && d.PerDayTone == PerDayTones.None);
    }

    [Fact]
    public void On_the_first_day_of_the_month_the_chart_has_started_and_today_is_marked()
    {
        var onFirst = Calculate(BudgetOf(2024, 1, 2900m), Feb2024, Feb2024);

        onFirst.Chart.Points[0].Actual.ShouldBe(2900m);
        onFirst.Chart.TodayDay.ShouldBe(1);
        onFirst.Days[0].IsToday.ShouldBeTrue();
    }

    public static TheoryData<string, decimal, string[]> ParityScenarios =>
        new() { { "ample budget", 3000m, AmpleBudgetGolden }, { "overspent budget", 500m, OverspentBudgetGolden } };

    [Theory]
    [MemberData(nameof(ParityScenarios))]
    public void Matches_the_old_application_on_a_fully_elapsed_month(string scenario, decimal income, string[] golden)
    {
        _ = scenario;
        var result = Calculate(
            [
                Deposit(2024, 1, 3, income),
                Withdrawal(2024, 1, 4, 200m, "Loan", ["MonthlyInstallment"], "Expense account"),
                Withdrawal(2024, 2, 1, 12.50m, "Bakery"),
                Withdrawal(2024, 2, 1, 7.25m, "bakery"),
                Withdrawal(2024, 2, 1, 30m, "Albert Heijn"),
                Withdrawal(2024, 2, 2, 150m, "Rent split", ["MonthlyInstallment"]),
                Withdrawal(2024, 2, 3, 45.10m, "Groceries"),
                Withdrawal(2024, 2, 3, 99.99m, "To expense payee", null, "Expense account"),
                Withdrawal(2024, 2, 5, 250m, "Phone"),
                Withdrawal(2024, 2, 8, 400m, "Furniture"),
                Withdrawal(2024, 2, 15, 5m, ""),
                Withdrawal(2024, 2, 29, 60m, "Z-shop"),
                Withdrawal(2024, 2, 29, 1m, "a-shop"),
                Withdrawal(2024, 2, 29, 2m, "M-shop"),
            ],
            Feb2024,
            new DateOnly(2026, 11, 13)
        );

        result.Days.Count.ShouldBe(golden.Length);
        for (var i = 0; i < golden.Length; i++)
        {
            var expected = golden[i].Split('|');
            var day = result.Days[i];

            day.Day.ShouldBe(int.Parse(expected[0], CultureInfo.InvariantCulture), $"day {i + 1}");
            day.Amount.ShouldBe(Decimal(expected[1]), $"amount of day {i + 1}");
            day.Description.ShouldBe(expected[2], $"description of day {i + 1}");
            day.CumulativeBudget.ShouldBe(Decimal(expected[3]), $"budget of day {i + 1}");
            day.RunningTotal.ShouldBe(Decimal(expected[4]), $"running total of day {i + 1}");
            day.PerDay.ShouldBe(Decimal(expected[5]), $"per day of day {i + 1}");
            var (tone, alpha) = ParseRgba(expected[6]);
            day.PerDayTone.ShouldBe(tone, $"per day tone of day {i + 1}");
            day.PerDayAlpha.ShouldBe(alpha, $"per day alpha of day {i + 1}");
            ParseRgba(expected[7]).Alpha.ShouldBe(day.AmountAlpha, $"amount alpha of day {i + 1}");
        }
    }

    private static decimal Decimal(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static (string Tone, decimal Alpha) ParseRgba(string value)
    {
        var parts = value["rgba(".Length..^1].Split(',', StringSplitOptions.TrimEntries);
        return (parts[0] == "0" ? PerDayTones.Green : PerDayTones.Red, Decimal(parts[3]));
    }

    /// <summary>Produced by the old DataProcessor on the same synthetic data: day|amount|description|budget|running|perDay|perDay colour|amount colour.</summary>
    private static readonly string[] AmpleBudgetGolden =
    [
        "1|49.75|Albert Heijn, Bakery|96.55|49.75|46.80|rgba(0, 255, 0, 0.02)|rgba(255, 0, 0, 0.06)",
        "2|0||193.10|49.75|143.35|rgba(0, 255, 0, 0.07)|rgba(255, 0, 0, 0.00)",
        "3|45.10|Groceries|289.66|94.85|194.81|rgba(0, 255, 0, 0.10)|rgba(255, 0, 0, 0.06)",
        "4|0||386.21|94.85|291.36|rgba(0, 255, 0, 0.15)|rgba(255, 0, 0, 0.00)",
        "5|250.00|Phone|482.76|344.85|137.91|rgba(0, 255, 0, 0.07)|rgba(255, 0, 0, 0.31)",
        "6|0||579.31|344.85|234.46|rgba(0, 255, 0, 0.12)|rgba(255, 0, 0, 0.00)",
        "7|0||675.86|344.85|331.01|rgba(0, 255, 0, 0.17)|rgba(255, 0, 0, 0.00)",
        "8|400.00|Furniture|772.41|744.85|27.56|rgba(0, 255, 0, 0.01)|rgba(255, 0, 0, 0.50)",
        "9|0||868.97|744.85|124.12|rgba(0, 255, 0, 0.06)|rgba(255, 0, 0, 0.00)",
        "10|0||965.52|744.85|220.67|rgba(0, 255, 0, 0.11)|rgba(255, 0, 0, 0.00)",
        "11|0||1062.07|744.85|317.22|rgba(0, 255, 0, 0.16)|rgba(255, 0, 0, 0.00)",
        "12|0||1158.62|744.85|413.77|rgba(0, 255, 0, 0.21)|rgba(255, 0, 0, 0.00)",
        "13|0||1255.17|744.85|510.32|rgba(0, 255, 0, 0.26)|rgba(255, 0, 0, 0.00)",
        "14|0||1351.72|744.85|606.87|rgba(0, 255, 0, 0.31)|rgba(255, 0, 0, 0.00)",
        "15|5.00||1448.28|749.85|698.43|rgba(0, 255, 0, 0.35)|rgba(255, 0, 0, 0.01)",
        "16|0||1544.83|749.85|794.98|rgba(0, 255, 0, 0.40)|rgba(255, 0, 0, 0.00)",
        "17|0||1641.38|749.85|891.53|rgba(0, 255, 0, 0.45)|rgba(255, 0, 0, 0.00)",
        "18|0||1737.93|749.85|988.08|rgba(0, 255, 0, 0.50)|rgba(255, 0, 0, 0.00)",
        "19|0||1834.48|749.85|1084.63|rgba(0, 255, 0, 0.55)|rgba(255, 0, 0, 0.00)",
        "20|0||1931.03|749.85|1181.18|rgba(0, 255, 0, 0.59)|rgba(255, 0, 0, 0.00)",
        "21|0||2027.59|749.85|1277.74|rgba(0, 255, 0, 0.64)|rgba(255, 0, 0, 0.00)",
        "22|0||2124.14|749.85|1374.29|rgba(0, 255, 0, 0.69)|rgba(255, 0, 0, 0.00)",
        "23|0||2220.69|749.85|1470.84|rgba(0, 255, 0, 0.74)|rgba(255, 0, 0, 0.00)",
        "24|0||2317.24|749.85|1567.39|rgba(0, 255, 0, 0.79)|rgba(255, 0, 0, 0.00)",
        "25|0||2413.79|749.85|1663.94|rgba(0, 255, 0, 0.84)|rgba(255, 0, 0, 0.00)",
        "26|0||2510.34|749.85|1760.49|rgba(0, 255, 0, 0.89)|rgba(255, 0, 0, 0.00)",
        "27|0||2606.90|749.85|1857.05|rgba(0, 255, 0, 0.93)|rgba(255, 0, 0, 0.00)",
        "28|0||2703.45|749.85|1953.60|rgba(0, 255, 0, 0.98)|rgba(255, 0, 0, 0.00)",
        "29|63.00|a-shop, M-shop, Z-shop|2800.00|812.85|1987.15|rgba(0, 255, 0, 1.00)|rgba(255, 0, 0, 0.08)",
    ];

    private static readonly string[] OverspentBudgetGolden =
    [
        "1|49.75|Albert Heijn, Bakery|10.34|49.75|-39.41|rgba(255, 0, 0, 0.03)|rgba(255, 0, 0, 0.06)",
        "2|0||20.69|49.75|-29.06|rgba(255, 0, 0, 0.02)|rgba(255, 0, 0, 0.00)",
        "3|45.10|Groceries|31.03|94.85|-63.82|rgba(255, 0, 0, 0.05)|rgba(255, 0, 0, 0.06)",
        "4|0||41.38|94.85|-53.47|rgba(255, 0, 0, 0.04)|rgba(255, 0, 0, 0.00)",
        "5|250.00|Phone|51.72|344.85|-293.13|rgba(255, 0, 0, 0.22)|rgba(255, 0, 0, 0.31)",
        "6|0||62.07|344.85|-282.78|rgba(255, 0, 0, 0.21)|rgba(255, 0, 0, 0.00)",
        "7|0||72.41|344.85|-272.44|rgba(255, 0, 0, 0.21)|rgba(255, 0, 0, 0.00)",
        "8|400.00|Furniture|82.76|744.85|-662.09|rgba(255, 0, 0, 0.50)|rgba(255, 0, 0, 0.50)",
        "9|0||93.10|744.85|-651.75|rgba(255, 0, 0, 0.49)|rgba(255, 0, 0, 0.00)",
        "10|0||103.45|744.85|-641.40|rgba(255, 0, 0, 0.48)|rgba(255, 0, 0, 0.00)",
        "11|0||113.79|744.85|-631.06|rgba(255, 0, 0, 0.48)|rgba(255, 0, 0, 0.00)",
        "12|0||124.14|744.85|-620.71|rgba(255, 0, 0, 0.47)|rgba(255, 0, 0, 0.00)",
        "13|0||134.48|744.85|-610.37|rgba(255, 0, 0, 0.46)|rgba(255, 0, 0, 0.00)",
        "14|0||144.83|744.85|-600.02|rgba(255, 0, 0, 0.45)|rgba(255, 0, 0, 0.00)",
        "15|5.00||155.17|749.85|-594.68|rgba(255, 0, 0, 0.45)|rgba(255, 0, 0, 0.01)",
        "16|0||165.52|749.85|-584.33|rgba(255, 0, 0, 0.44)|rgba(255, 0, 0, 0.00)",
        "17|0||175.86|749.85|-573.99|rgba(255, 0, 0, 0.43)|rgba(255, 0, 0, 0.00)",
        "18|0||186.21|749.85|-563.64|rgba(255, 0, 0, 0.43)|rgba(255, 0, 0, 0.00)",
        "19|0||196.55|749.85|-553.30|rgba(255, 0, 0, 0.42)|rgba(255, 0, 0, 0.00)",
        "20|0||206.90|749.85|-542.95|rgba(255, 0, 0, 0.41)|rgba(255, 0, 0, 0.00)",
        "21|0||217.24|749.85|-532.61|rgba(255, 0, 0, 0.40)|rgba(255, 0, 0, 0.00)",
        "22|0||227.59|749.85|-522.26|rgba(255, 0, 0, 0.39)|rgba(255, 0, 0, 0.00)",
        "23|0||237.93|749.85|-511.92|rgba(255, 0, 0, 0.39)|rgba(255, 0, 0, 0.00)",
        "24|0||248.28|749.85|-501.57|rgba(255, 0, 0, 0.38)|rgba(255, 0, 0, 0.00)",
        "25|0||258.62|749.85|-491.23|rgba(255, 0, 0, 0.37)|rgba(255, 0, 0, 0.00)",
        "26|0||268.97|749.85|-480.88|rgba(255, 0, 0, 0.36)|rgba(255, 0, 0, 0.00)",
        "27|0||279.31|749.85|-470.54|rgba(255, 0, 0, 0.36)|rgba(255, 0, 0, 0.00)",
        "28|0||289.66|749.85|-460.19|rgba(255, 0, 0, 0.35)|rgba(255, 0, 0, 0.00)",
        "29|63.00|a-shop, M-shop, Z-shop|300.00|812.85|-512.85|rgba(255, 0, 0, 0.39)|rgba(255, 0, 0, 0.08)",
    ];
}
