using Burndown.Common;
using Microsoft.Extensions.Options;

namespace Burndown.Web;

/// <summary>Settings shared by every slice, bound from the <c>Burndown</c> configuration section.</summary>
internal sealed class BurndownOptions
{
    public const string Section = "Burndown";

    /// <summary>Gets or sets the IANA id of the zone that defines "today" and the current month.</summary>
    public string? TimeZone { get; set; }
}

/// <summary>Validates <see cref="BurndownOptions"/> without touching the network.</summary>
internal sealed class BurndownOptionsValidator : IValidateOptions<BurndownOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, BurndownOptions options) =>
        AppClock.TryResolveZone(options.TimeZone, out _)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{BurndownOptions.Section}:{nameof(BurndownOptions.TimeZone)} must be a known time zone id, for example Europe/Brussels."
            );
}
