namespace Burndown.Models;

public class Expense {
    public DateTime Date { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public required string[] Tags { get; set; }
    public string? DestinationType { get; set; }
}