using Burndown.Entry;
using Shouldly;
using static Burndown.UnitTests.Entry.EntryTestData;

namespace Burndown.UnitTests.Entry;

public class TimestampSequencerTests
{
    private static DateTime Local(int month, int day, int hour, int minute) =>
        new(2026, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    [Fact]
    public void Keeps_increasing_times_as_entered()
    {
        var sequencer = new TimestampSequencer(Brussels);

        var first = sequencer.Next(Local(5, 15, 9, 0));
        var second = sequencer.Next(Local(5, 15, 9, 1));

        first.Value.Bumped.ShouldBeFalse();
        second.Value.Bumped.ShouldBeFalse();
        second.Value.Local.ShouldBe(Local(5, 15, 9, 1));
        second.Value.Timestamp.ShouldBe(new DateTimeOffset(2026, 5, 15, 9, 1, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Bumps_an_equal_or_earlier_time_one_minute_after_the_previous_row()
    {
        var sequencer = new TimestampSequencer(Brussels);
        sequencer.Next(Local(5, 15, 9, 30));

        var equal = sequencer.Next(Local(5, 15, 9, 30));
        var earlier = sequencer.Next(Local(5, 15, 8, 0));

        equal.Value.Local.ShouldBe(Local(5, 15, 9, 31));
        equal.Value.Bumped.ShouldBeTrue();
        earlier.Value.Local.ShouldBe(Local(5, 15, 9, 32));
        earlier.Value.Bumped.ShouldBeTrue();
    }

    [Fact]
    public void Compares_only_rows_on_the_same_date()
    {
        var sequencer = new TimestampSequencer(Brussels);
        sequencer.Next(Local(5, 15, 18, 0));

        var otherDate = sequencer.Next(Local(5, 14, 9, 0));
        var sameDateAgain = sequencer.Next(Local(5, 15, 18, 0));

        otherDate.Value.Bumped.ShouldBeFalse();
        sameDateAgain.Value.Local.ShouldBe(Local(5, 15, 18, 1));
    }

    [Fact]
    public void Fails_instead_of_rolling_into_the_next_day()
    {
        var sequencer = new TimestampSequencer(Brussels);
        sequencer.Next(Local(5, 15, 23, 59));

        var result = sequencer.Next(Local(5, 15, 23, 59));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Fields!.ShouldContainKey("dateTime");
    }

    [Fact]
    public void Does_not_advance_the_last_time_after_a_failure()
    {
        var sequencer = new TimestampSequencer(Brussels);
        sequencer.Next(Local(5, 15, 23, 59));
        sequencer.Next(Local(5, 15, 23, 59)).IsSuccess.ShouldBeFalse();

        sequencer.Next(Local(5, 15, 23, 59)).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void Seeded_times_push_the_next_row_on_that_date_one_minute_later_and_keep_the_latest()
    {
        var sequencer = new TimestampSequencer(Brussels);
        sequencer.Seed(Local(5, 15, 9, 30));
        sequencer.Seed(Local(5, 15, 9, 10));

        var next = sequencer.Next(Local(5, 15, 9, 0));

        next.Value.Local.ShouldBe(Local(5, 15, 9, 31));
        next.Value.Bumped.ShouldBeTrue();
        sequencer.Next(Local(5, 14, 9, 0)).Value.Bumped.ShouldBeFalse();
    }

    [Fact]
    public void Rejects_a_time_in_the_spring_forward_gap()
    {
        var sequencer = new TimestampSequencer(Brussels);

        var result = sequencer.Next(Local(3, 29, 2, 30));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Fields!.ShouldContainKey("dateTime");
    }

    [Fact]
    public void Rejects_a_bump_that_lands_in_the_spring_forward_gap()
    {
        var sequencer = new TimestampSequencer(Brussels);
        sequencer.Next(Local(3, 29, 1, 59));

        sequencer.Next(Local(3, 29, 1, 59)).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void Bumps_after_the_latest_row_even_when_it_is_past_the_gap()
    {
        var sequencer = new TimestampSequencer(Brussels);
        sequencer.Next(Local(3, 29, 1, 59));
        sequencer.Next(Local(3, 29, 3, 0));

        sequencer.Next(Local(3, 29, 1, 59)).Value.Local.ShouldBe(Local(3, 29, 3, 1));
    }

    [Fact]
    public void Uses_the_correct_offset_on_both_sides_of_the_spring_forward_change()
    {
        var sequencer = new TimestampSequencer(Brussels);

        sequencer.Next(Local(3, 29, 1, 59)).Value.Timestamp.Offset.ShouldBe(TimeSpan.FromHours(1));
        sequencer.Next(Local(3, 29, 3, 0)).Value.Timestamp.Offset.ShouldBe(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Takes_the_first_occurrence_of_an_ambiguous_time_in_the_fall_back_hour()
    {
        var sequencer = new TimestampSequencer(Brussels);

        var result = sequencer.Next(Local(10, 25, 2, 30));

        result.Value.Timestamp.Offset.ShouldBe(TimeSpan.FromHours(2));
        result.Value.Timestamp.UtcDateTime.ShouldBe(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Keeps_local_order_through_the_fall_back_hour()
    {
        var sequencer = new TimestampSequencer(Brussels);

        var a = sequencer.Next(Local(10, 25, 2, 30)).Value.Timestamp;
        var b = sequencer.Next(Local(10, 25, 2, 30)).Value.Timestamp;

        b.ShouldBeGreaterThan(a);
    }
}
