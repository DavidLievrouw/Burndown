namespace Burndown.Common;

/// <summary>
/// The single source of "now", "today" and the current month in the configured time zone.
/// Business logic must never read the system clock or the browser clock directly.
/// </summary>
public sealed class AppClock
{
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="AppClock"/> class.</summary>
    /// <param name="timeProvider">The underlying time source.</param>
    /// <param name="zone">The configured time zone.</param>
    public AppClock(TimeProvider timeProvider, TimeZoneInfo zone)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        Zone = zone ?? throw new ArgumentNullException(nameof(zone));
    }

    /// <summary>Gets the configured time zone.</summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>Gets the current instant expressed in the configured zone, with its real offset.</summary>
    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), Zone);

    /// <summary>Gets the current calendar date in the configured zone.</summary>
    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    /// <summary>Gets the first day of the current month in the configured zone.</summary>
    public DateOnly CurrentMonth => new(Today.Year, Today.Month, 1);

    /// <summary>Resolves an IANA (or Windows) time zone id.</summary>
    /// <param name="id">The time zone id, for example <c>Europe/Brussels</c>.</param>
    /// <param name="zone">The resolved zone, or <see langword="null"/> when the id is unknown.</param>
    /// <returns><see langword="true"/> when the id resolved to a zone.</returns>
    public static bool TryResolveZone(string? id, out TimeZoneInfo? zone)
    {
        zone = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
