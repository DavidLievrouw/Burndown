using System.Globalization;
using Burndown.Common;

namespace Burndown.Entry;

/// <summary>The timestamp a row is sent with.</summary>
/// <param name="Timestamp">The instant with the offset of the configured zone.</param>
/// <param name="Local">The effective local date and time, minute precision.</param>
/// <param name="Bumped">Whether the time was moved from the one entered, to keep the entry order or to not be in the future.</param>
internal sealed record SequencedTime(DateTimeOffset Timestamp, DateTime Local, bool Bumped);

/// <summary>
/// Keeps rows of one batch that share a calendar date in non-decreasing time order, so Firefly shows them in the
/// entered order (it breaks ties by creation order). Equal times are allowed, as Firefly allows them: only a row that
/// is earlier than the nearest preceding row on the same date is moved up to that row's time. Rows on other dates are
/// ignored. Rows dated today are never placed after now, because Firefly leaves future transactions out of the current
/// balance: <see cref="Place"/> shifts the same-day chain back so its last row lands at now.
/// </summary>
internal sealed class TimestampSequencer
{
    private readonly TimeZoneInfo _zone;
    private readonly DateTime _now;
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    private readonly Dictionary<DateOnly, DateTime> _last = [];

    /// <summary>Initializes a new instance of the <see cref="TimestampSequencer"/> class.</summary>
    /// <param name="zone">The configured time zone.</param>
    /// <param name="now">The current local date and time in the configured zone, of unspecified kind.</param>
    public TimestampSequencer(TimeZoneInfo zone, DateTime now)
    {
        _zone = zone;
        _now = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Unspecified);
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

    /// <summary>
    /// Places all rows of a batch. Rows dated today that would end up after now are shifted back so that the last of
    /// them lands at most at now. Rows that are a minute or more apart stay a minute apart where there is room, and
    /// rows with equal times stay equal; where there is no room left (after the previous row of today), rows share a
    /// time instead of being rejected.
    /// </summary>
    /// <param name="locals">
    /// The local date and time of each row as entered (minute precision, unspecified kind), or <see langword="null"/>
    /// for a row that is not valid and has no time.
    /// </param>
    /// <returns>One entry per row: its placement, or <see langword="null"/> where <paramref name="locals"/> has none.</returns>
    public IReadOnlyList<Result<SequencedTime>?> Place(IReadOnlyList<DateTime?> locals)
    {
        var results = new Result<SequencedTime>?[locals.Count];
        var today = DateOnly.FromDateTime(_now);
        var adjusted = locals.ToArray();
        var todayRows = Enumerable
            .Range(0, locals.Count)
            .Where(i => locals[i] is { } local && DateOnly.FromDateTime(local) == today)
            .ToList();

        var floor = _last.TryGetValue(today, out var last) ? last : today.ToDateTime(TimeOnly.MinValue);
        var forward = new DateTime[todayRows.Count];
        var previous = floor;
        for (var i = 0; i < todayRows.Count; i++)
        {
            forward[i] = Max(adjusted[todayRows[i]]!.Value, previous);
            previous = forward[i];
        }

        var limit = _now;
        for (var i = todayRows.Count - 1; i >= 0; i--)
        {
            var placed = Max(floor, Min(forward[i], limit));
            adjusted[todayRows[i]] = placed;
            var gap = i > 0 ? Min(OneMinute, forward[i] - forward[i - 1]) : TimeSpan.Zero;
            limit = placed - gap;
        }

        for (var index = 0; index < locals.Count; index++)
        {
            if (adjusted[index] is not { } local)
            {
                continue;
            }

            var placed = Next(local);
            results[index] =
                placed.IsSuccess && local != locals[index]
                    ? Result<SequencedTime>.Success(placed.Value with { Bumped = true })
                    : placed;
        }

        return results;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    /// <summary>Places the next row of the batch.</summary>
    /// <param name="local">The local date and time as entered, minute precision, of unspecified kind.</param>
    /// <returns>The effective timestamp, or a validation failure on <c>dateTime</c> when the row cannot be placed.</returns>
    public Result<SequencedTime> Next(DateTime local)
    {
        var date = DateOnly.FromDateTime(local);
        var effective = local;
        var bumped = false;
        var isToday = date == DateOnly.FromDateTime(_now);
        if (isToday && effective > _now)
        {
            effective = _now;
            bumped = true;
        }

        if (_last.TryGetValue(date, out var last) && effective < last)
        {
            effective = last;
            bumped = true;
        }

        if (isToday && effective > _now)
        {
            return Fail(
                $"An earlier row today is dated after now ({_now.ToString("HH:mm", CultureInfo.InvariantCulture)}). Rows dated today cannot be in the future."
            );
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
