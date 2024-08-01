namespace Burndown.Models;

public class QuickExpense {
    public required string Account { get; set; }
    public DateTime Date { get; set; }
    public required string Description { get; set; }
    public required decimal Amount { get; set; }
    public required string Category { get; set; }
    public required string Target { get; set; }
    public int? Budget { get; set; }
}