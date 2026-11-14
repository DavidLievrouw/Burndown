using System.Globalization;
using Burndown.Common;

namespace Burndown.Entry;

/// <summary>The timestamp a row is sent with.</summary>
/// <param name="Timestamp">The instant with the offset of the configured zone.</param>
/// <param name="Local">The effective local date and time, minute precision.</param>
/// <param name="Bumped">Whether the time was moved later than entered to keep the entry order.</param>
internal sealed record SequencedTime(DateTimeOffset Timestamp, DateTime Local, bool Bumped);

/// <summary>
/// Keeps rows of one batch that share a calendar date strictly increasing, so Firefly shows them in the entered order.
/// It compares each row with the nearest preceding row on the same date and ignores rows on other dates.
/// </summary>
internal sealed class TimestampSequencer
{
    private readonly TimeZoneInfo _zone;
    private readonly Dictionary<DateOnly, DateTime> _last = [];

    /// <summary>Initializes a new instance of the <see cref="TimestampSequencer"/> class.</summary>
    /// <param name="zone">The configured time zone.</param>
    public TimestampSequencer(TimeZoneInfo zone)
    {
        _zone = zone;
    }

    /// <summary>Records the time of a row that was already created, so that later rows are placed after it.</summary>
    /// <param name="local">The effective local date and time of the created row.</param>
    public void Seed(DateTime local)
    {
        var date = DateOnly.FromDateTime(local);
        if (!_last.TryGetValue(date, out var last) || local > last)
        {
            _last[date] = local;
        }
    }

    /// <summary>Places the next row of the batch.</summary>
    /// <param name="local">The local date and time as entered, minute precision, of unspecified kind.</param>
    /// <returns>The effective timestamp, or a validation failure on <c>dateTime</c> when the row cannot be placed.</returns>
    public Result<SequencedTime> Next(DateTime local)
    {
        var date = DateOnly.FromDateTime(local);
        var effective = local;
        var bumped = false;
        if (_last.TryGetValue(date, out var last) && effective <= last)
        {
            effective = last.AddMinutes(1);
            bumped = true;
        }

        if (DateOnly.FromDateTime(effective) != date)
        {
            return Fail("No later time is left on this date to keep this row after the previous one.");
        }

        if (_zone.IsInvalidTime(effective))
        {
            return Fail(
                $"{effective.ToString("HH:mm", CultureInfo.InvariantCulture)} does not exist on this date because the clock changes."
            );
        }

        // An ambiguous time (clocks go back) resolves to its first occurrence, the one with the larger offset.
        var offset = _zone.IsAmbiguousTime(effective)
            ? _zone.GetAmbiguousTimeOffsets(effective).Max()
            : _zone.GetUtcOffset(effective);
        _last[date] = effective;
        return Result<SequencedTime>.Success(
            new SequencedTime(new DateTimeOffset(effective, offset), effective, bumped)
        );
    }

    private static Result<SequencedTime> Fail(string message) =>
        Result<SequencedTime>.Failure(
            new Error(
                ErrorCodes.Validation,
                "The row cannot be placed in time.",
                new Dictionary<string, string[]> { ["dateTime"] = [message] }
            )
        );
}
