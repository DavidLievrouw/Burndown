namespace Burndown.Models;

public class QuickTransfer {
    public required string FromAccount { get; set; }
    public required string ToAccount { get; set; }
    public DateTime Date { get; set; }
    public required string Description { get; set; }
    public required decimal Amount { get; set; }
}