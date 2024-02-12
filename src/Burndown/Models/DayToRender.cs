namespace Burndown.Models;

public class DayToRender {
    public DateTime Date { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public decimal Budget { get; set; }
    public decimal RunningTotal { get; set; }
    public decimal PerDay { get; set; }
    public required string PerDayColor { get; set; }
    public required string AmountColor { get; set; }
}