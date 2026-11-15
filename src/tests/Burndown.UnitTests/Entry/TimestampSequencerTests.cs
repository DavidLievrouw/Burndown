using Burndown.Entry;
using Shouldly;
using static Burndown.UnitTests.Entry.EntryTestData;

namespace Burndown.UnitTests.Entry;

public class TimestampSequencerTests
{
    private static DateTime Local(int month, int day, int hour, int minute) =>
        new(2026, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    private static TimestampSequencer Create() => new(Brussels, Local(12, 31, 12, 0));

    private static TimestampSequencer CreateAt(DateTime now) => new(Brussels, now);

    [Fact]
    public void Keeps_increasing_times_as_entered()
    {
        var sequencer = Create();

        var first = sequencer.Next(Local(5, 15, 9, 0));
        var second = sequencer.Next(Local(5, 15, 9, 1));

        first.Value.Bumped.ShouldBeFalse();
        second.Value.Bumped.ShouldBeFalse();
        second.Value.Local.ShouldBe(Local(5, 15, 9, 1));
        second.Value.Timestamp.ShouldBe(new DateTimeOffset(2026, 5, 15, 9, 1, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Allows_an_equal_time_and_moves_an_earlier_time_up_to_the_previous_row()
    {
        var sequencer = Create();
        sequencer.Next(Local(5, 15, 9, 30));

        var equal = sequencer.Next(Local(5, 15, 9, 30));
        var earlier = sequencer.Next(Local(5, 15, 8, 0));

        equal.Value.Local.ShouldBe(Local(5, 15, 9, 30));
        equal.Value.Bumped.ShouldBeFalse();
        earlier.Value.Local.ShouldBe(Local(5, 15, 9, 30));
        earlier.Value.Bumped.ShouldBeTrue();
    }

    [Fact]
    public void Compares_only_rows_on_the_same_date()
    {
        var sequencer = Create();
        sequencer.Next(Local(5, 15, 18, 0));

        var otherDate = sequencer.Next(Local(5, 14, 9, 0));
        var sameDateAgain = sequencer.Next(Local(5, 15, 17, 0));

        otherDate.Value.Bumped.ShouldBeFalse();
        sameDateAgain.Value.Local.ShouldBe(Local(5, 15, 18, 0));
    }

    [Fact]
    public void Allows_equal_times_at_the_end_of_the_day()
    {
        var sequencer = Create();
        sequencer.Next(Local(5, 15, 23, 59));

        var result = sequencer.Next(Local(5, 15, 23, 59));

        result.Value.Local.ShouldBe(Local(5, 15, 23, 59));
        result.Value.Bumped.ShouldBeFalse();
    }

    [Fact]
    public void Seeded_times_move_an_earlier_row_on_that_date_up_to_the_latest_one()
    {
        var sequencer = Create();
        sequencer.Seed(Local(5, 15, 9, 30));
        sequencer.Seed(Local(5, 15, 9, 10));

        var next = sequencer.Next(Local(5, 15, 9, 0));

        next.Value.Local.ShouldBe(Local(5, 15, 9, 30));
        next.Value.Bumped.ShouldBeTrue();
        sequencer.Next(Local(5, 15, 9, 30)).Value.Bumped.ShouldBeFalse();
        sequencer.Next(Local(5, 14, 9, 0)).Value.Bumped.ShouldBeFalse();
    }

    [Fact]
    public void Rejects_a_time_in_the_spring_forward_gap()
    {
        var sequencer = Create();

        var result = sequencer.Next(Local(3, 29, 2, 30));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Fields!.ShouldContainKey("dateTime");
    }

    [Fact]
    public void Moves_up_to_the_latest_row_even_when_it_is_past_the_gap()
    {
        var sequencer = Create();
        sequencer.Next(Local(3, 29, 1, 59));
        sequencer.Next(Local(3, 29, 3, 0));

        sequencer.Next(Local(3, 29, 1, 59)).Value.Local.ShouldBe(Local(3, 29, 3, 0));
    }

    [Fact]
    public void Uses_the_correct_offset_on_both_sides_of_the_spring_forward_change()
    {
        var sequencer = Create();

        sequencer.Next(Local(3, 29, 1, 59)).Value.Timestamp.Offset.ShouldBe(TimeSpan.FromHours(1));
        sequencer.Next(Local(3, 29, 3, 0)).Value.Timestamp.Offset.ShouldBe(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Takes_the_first_occurrence_of_an_ambiguous_time_in_the_fall_back_hour()
    {
        var sequencer = Create();

        var result = sequencer.Next(Local(10, 25, 2, 30));

        result.Value.Timestamp.Offset.ShouldBe(TimeSpan.FromHours(2));
        result.Value.Timestamp.UtcDateTime.ShouldBe(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Never_goes_back_in_time_through_the_fall_back_hour()
    {
        var sequencer = Create();

        var a = sequencer.Next(Local(10, 25, 2, 30)).Value.Timestamp;
        var b = sequencer.Next(Local(10, 25, 2, 30)).Value.Timestamp;

        b.ShouldBeGreaterThanOrEqualTo(a);
    }

    [Fact]
    public void Caps_a_time_later_today_at_now()
    {
        var sequencer = CreateAt(Local(5, 15, 11, 23));

        var result = sequencer.Next(Local(5, 15, 11, 40));

        result.Value.Local.ShouldBe(Local(5, 15, 11, 23));
        result.Value.Bumped.ShouldBeTrue();
    }

    [Fact]
    public void Ignores_the_seconds_of_now()
    {
        var sequencer = CreateAt(new DateTime(2026, 5, 15, 11, 23, 59, DateTimeKind.Unspecified));

        sequencer.Next(Local(5, 15, 11, 23)).Value.Bumped.ShouldBeFalse();
    }

    [Fact]
    public void Keeps_a_time_today_that_is_not_in_the_future()
    {
        var sequencer = CreateAt(Local(5, 15, 11, 23));

        var result = sequencer.Next(Local(5, 15, 11, 22));

        result.Value.Local.ShouldBe(Local(5, 15, 11, 22));
        result.Value.Bumped.ShouldBeFalse();
    }

    [Fact]
    public void Allows_several_rows_at_exactly_now()
    {
        var sequencer = CreateAt(Local(5, 15, 11, 23));

        sequencer.Next(Local(5, 15, 11, 23)).Value.Bumped.ShouldBeFalse();
        var second = sequencer.Next(Local(5, 15, 11, 23));
        var third = sequencer.Next(Local(5, 15, 11, 24));

        second.Value.Local.ShouldBe(Local(5, 15, 11, 23));
        second.Value.Bumped.ShouldBeFalse();
        third.Value.Local.ShouldBe(Local(5, 15, 11, 23));
        third.Value.Bumped.ShouldBeTrue();
    }

    [Fact]
    public void Fails_when_an_earlier_row_today_is_already_after_now()
    {
        var sequencer = CreateAt(Local(5, 15, 11, 23));
        sequencer.Seed(Local(5, 15, 11, 30));

        var result = sequencer.Next(Local(5, 15, 11, 22));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Fields!.ShouldContainKey("dateTime");
    }

    [Fact]
    public void Does_not_cap_tomorrow_or_other_dates()
    {
        var sequencer = CreateAt(Local(5, 15, 11, 23));

        sequencer.Next(Local(5, 16, 9, 0)).Value.Local.ShouldBe(Local(5, 16, 9, 0));
        sequencer.Next(Local(5, 16, 9, 1)).Value.Bumped.ShouldBeFalse();
    }

    [Fact]
    public void Place_shifts_the_tail_of_a_same_day_chain_back_so_the_last_row_lands_at_now()
    {
        var sequencer = CreateAt(Local(5, 15, 11, 23));

        var placed = sequencer.Place([
            Local(5, 15, 11, 20),
            Local(5, 15, 11, 21),
            Local(5, 15, 11, 22),
            Local(5, 15, 11, 23),
            Local(5, 15, 11, 24),
        ]);

        placed.Select(p => p!.Value.Value.Local.Minute).ShouldBe([19, 20, 21, 22, 23]);
        placed.Select(p => p!.Value.Value.Bumped).ShouldBe([true, true, true, true, true]);
    }

    [Fact]
    public void Place_leaves_earlier_rows_alone_when_only_the_end_of_the_chain_overflows()
    {
        var sequencer = CreateAt(Local(5, 15, 11, 23));

        var placed = sequencer.Place([Local(5, 15, 9, 0), Local(5, 15, 11, 22), Local(5, 15, 11, 30)]);

        placed
            .Select(p => p!.Value.Value.Local)
            .ShouldBe([Local(5, 15, 9, 0), Local(5, 15, 11, 22), Local(5, 15, 11, 23)]);
        placed.Select(p => p!.Value.Value.Bumped).ShouldBe([false, false, true]);
    }

    [Fact]
    public void Place_does_not_move_rows_that_are_already_not_in_the_future()
    {
        var sequencer = CreateAt(Local(5, 15, 11, 23));

        var placed = sequencer.Place([Local(5, 15, 11, 21), Local(5, 15, 11, 22), Local(5, 15, 11, 23)]);

        placed.Select(p => p!.Value.Value.Local.Minute).ShouldBe([21, 22, 23]);
        placed.Select(p => p!.Value.Value.Bumped).ShouldBe([false, false, false]);
    }

    [Fact]
    public void Place_keeps_equal_times_equal()
    {
        var sequencer = CreateAt(Local(5, 15, 11, 23));

        var placed = sequencer.Place([Local(5, 15, 11, 22), Local(5, 15, 11, 22), Local(5, 15, 11, 22)]);

        placed.Select(p => p!.Value.Value.Local.Minute).ShouldBe([22, 22, 22]);
        placed.Select(p => p!.Value.Value.Bumped).ShouldBe([false, false, false]);
    }

    [Fact]
    public void Place_shifts_only_as_far_as_needed_when_equal_times_are_in_the_chain()
    {
        var sequencer = CreateAt(Local(5, 15, 11, 23));

        var placed = sequencer.Place([Local(5, 15, 11, 23), Local(5, 15, 11, 24), Local(5, 15, 11, 24)]);

        placed.Select(p => p!.Value.Value.Local.Minute).ShouldBe([22, 23, 23]);
    }

    [Fact]
    public void Place_never_goes_before_a_seeded_previous_row_and_shares_the_time_when_there_is_no_room()
    {
        var sequencer = CreateAt(Local(5, 15, 11, 23));
        sequencer.Seed(Local(5, 15, 11, 21));

        var placed = sequencer.Place([Local(5, 15, 11, 40), Local(5, 15, 11, 41), Local(5, 15, 11, 42)]);

        placed.Select(p => p!.Value.Value.Local.Minute).ShouldBe([21, 22, 23]);

        var crowded = CreateAt(Local(5, 15, 11, 23));
        crowded.Seed(Local(5, 15, 11, 22));

        crowded
            .Place([Local(5, 15, 11, 40), Local(5, 15, 11, 41), Local(5, 15, 11, 42)])
            .Select(p => p!.Value.Value.Local.Minute)
            .ShouldBe([22, 22, 23]);
    }

    [Fact]
    public void Place_ignores_other_dates_and_missing_rows()
    {
        var sequencer = CreateAt(Local(5, 15, 11, 23));

        var placed = sequencer.Place([Local(5, 16, 9, 0), null, Local(5, 15, 11, 40), Local(5, 14, 23, 59)]);

        placed[0]!.Value.Value.Local.ShouldBe(Local(5, 16, 9, 0));
        placed[0]!.Value.Value.Bumped.ShouldBeFalse();
        placed[1].ShouldBeNull();
        placed[2]!.Value.Value.Local.ShouldBe(Local(5, 15, 11, 23));
        placed[3]!.Value.Value.Bumped.ShouldBeFalse();
    }

    [Fact]
    public void Place_shares_the_time_at_the_start_of_the_day_instead_of_rejecting()
    {
        var sequencer = CreateAt(Local(5, 15, 0, 1));

        var placed = sequencer.Place([Local(5, 15, 9, 0), Local(5, 15, 9, 1), Local(5, 15, 9, 2)]);

        placed.Select(p => p!.Value.Value.Local).ShouldBe([Local(5, 15, 0, 0), Local(5, 15, 0, 0), Local(5, 15, 0, 1)]);
    }

    [Fact]
    public void Uses_the_local_date_of_now_not_the_utc_date()
    {
        var sequencer = CreateAt(Local(5, 16, 0, 30));

        sequencer.Next(Local(5, 15, 23, 50)).Value.Bumped.ShouldBeFalse();
        sequencer.Next(Local(5, 16, 9, 0)).Value.Local.ShouldBe(Local(5, 16, 0, 30));
    }
}
