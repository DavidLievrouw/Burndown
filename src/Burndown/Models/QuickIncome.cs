namespace Burndown.Models;

public class QuickIncome {
    public required string Account { get; set; }
    public DateTime Date { get; set; }
    public required string Description { get; set; }
    public required decimal Amount { get; set; }
}