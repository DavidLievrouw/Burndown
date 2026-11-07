using System.Globalization;

namespace Burndown.Overview;

/// <summary>Overview settings, bound from the <c>Burndown:Overview</c> configuration section.</summary>
internal sealed class OverviewOptions
{
    public const string Section = "Burndown:Overview";

    /// <summary>Gets or sets the first month that can be viewed, formatted as <c>yyyy-MM</c>.</summary>
    public string? MinimumMonth { get; set; }

    /// <summary>Gets or sets the tag that marks monthly installments, which are not daily expenses.</summary>
    public string? MonthlyInstallmentTag { get; set; }

    /// <summary>Gets or sets the destination account type whose withdrawals are not counted as expenses.</summary>
    public string? ExcludedDestinationAccountType { get; set; }

    /// <summary>Parses <see cref="MinimumMonth"/> into the first day of that month.</summary>
    /// <param name="firstDay">The first day of the minimum month.</param>
    /// <returns><see langword="true"/> when the setting is a valid <c>yyyy-MM</c> value.</returns>
    public bool TryGetMinimumMonth(out DateOnly firstDay)
    {
        firstDay = default;
        return MinimumMonth is { Length: 7 }
            && DateOnly.TryParseExact(
                MinimumMonth + "-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out firstDay
            );
    }
}
