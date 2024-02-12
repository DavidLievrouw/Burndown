using System.Globalization;
using Burndown.Models;

namespace Burndown.Services;

public class DataProcessor {
    public IList<DayToRender> GetGridData(DateTime selectedMonth, decimal incomeOfPreviousMonth, IEnumerable<Expense> expenses) {
        var groupedExpenses = expenses
            .Where(e => !e.Tags.Contains("MonthlyInstallment")) // Exclude monthly installments
            .Where(e => e.DestinationType != "Expense account") // Exclude payments to expense accounts
            .GroupBy(e => e.Date.Date)
            .Select(
                g => new Expense {
                    Date = g.Key,
                    Description = string.Join(", ", g.Where(e => !string.IsNullOrEmpty(e.Description)).Select(e => e.Description)),
                    Amount = g.Sum(e => e.Amount),
                    Tags = g.SelectMany(e => e.Tags).Distinct().ToArray()
                }
            )
            .OrderBy(e => e.Date)
            .ToList();

        var dayBudget = incomeOfPreviousMonth / DateTime.DaysInMonth(selectedMonth.Year, selectedMonth.Month);

        var days = Enumerable
            .Range(1, DateTime.DaysInMonth(selectedMonth.Year, selectedMonth.Month))
            .Select(
                dayNumber => {
                    var groupedExpense = groupedExpenses.FirstOrDefault(e => e.Date.Day == dayNumber);
                    return new DayToRender {
                        Date = new DateTime(selectedMonth.Year, selectedMonth.Month, dayNumber),
                        Amount = groupedExpense?.Amount ?? 0,
                        Description = groupedExpense?.Description,
                        Budget = dayBudget * dayNumber,
                        PerDay = 0,
                        PerDayColor = "rgba(255, 255, 255, 255)",
                        AmountColor = "rgba(255, 255, 255, 255)"
                    };
                }
            )
            .Aggregate(
                new List<DayToRender>(),
                (list, day) => {
                    day.RunningTotal = (list.LastOrDefault()?.RunningTotal ?? 0) + day.Amount;
                    day.PerDay = day.Budget - day.RunningTotal;
                    list.Add(day);
                    return list;
                }
            )
            .ToList();

        return days
            .Select(
                day => {
                    day.PerDayColor = GetPerDayColor(days, day);
                    day.AmountColor = GetAmountColor(days, day);
                    return day;
                }
            )
            .ToList();
    }

    private static string GetPerDayColor(IList<DayToRender> days, DayToRender dayToRender) {
        if (dayToRender.Date > DateTime.UtcNow.Date) {
            return "rgba(255, 255, 255, 255)";
        }

        var filtered = days.Where(d => d.Date <= DateTime.UtcNow.Date).ToList();
        var range = filtered.Any()
            ? dayToRender.PerDay < 0
                ? Math.Abs(filtered.Min(d => d.PerDay))
                : filtered.Max(d => d.PerDay)
            : 1;

        if (dayToRender.PerDay >= 0) {
            var greenness = Math.Min(1, dayToRender.PerDay / range);
            return $"rgba(0, 255, 0, {greenness.ToString("0.00", CultureInfo.InvariantCulture)})";
        }

        var redness = Math.Min(1, Math.Abs(dayToRender.PerDay) / range) / 2;
        return $"rgba(255, 0, 0, {redness.ToString("0.00", CultureInfo.InvariantCulture)})";
    }

    private static string GetAmountColor(IList<DayToRender> days, DayToRender dayToRender) {
        var range = days.Any()
            ? days.Max(d => d.Amount)
            : 1;

        var redness = Math.Min(1, Math.Abs(dayToRender.Amount) / range) / 2;
        return $"rgba(255, 0, 0, {redness.ToString("0.00", CultureInfo.InvariantCulture)})";
    }
}