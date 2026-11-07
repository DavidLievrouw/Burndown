namespace Burndown.Entry;

/// <summary>Entry settings, bound from the <c>Burndown:Entry</c> configuration section.</summary>
internal sealed class EntryOptions
{
    public const string Section = "Burndown:Entry";

    /// <summary>Gets or sets the Firefly id of the cash account, the counterpart of every expense and income.</summary>
    public int CashAccountId { get; set; }

    /// <summary>Gets or sets how many months back a transaction date may lie.</summary>
    public int MaxBackdatedMonths { get; set; }

    /// <summary>Gets or sets the selectable expense categories, in their default order.</summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>Gets or sets the selectable target tags, in their default order.</summary>
    public List<string> TargetTags { get; set; } = [];

    /// <summary>Gets or sets the selectable budgets, in their default order.</summary>
    public List<BudgetOption> Budgets { get; set; } = [];
}

/// <summary>A selectable budget.</summary>
internal sealed class BudgetOption
{
    /// <summary>Gets or sets the Firefly budget id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public string? Name { get; set; }
}
