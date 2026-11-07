using System.Globalization;
using Burndown.Common;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Burndown.UnitTests.Common;

public class AppClockTests
{
    private static readonly TimeZoneInfo Brussels = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    private static AppClock CreateClock(string utcInstant, out FakeTimeProvider time)
    {
        time = new FakeTimeProvider(DateTimeOffset.Parse(utcInstant, CultureInfo.InvariantCulture));
        return new AppClock(time, Brussels);
    }

    [Fact]
    public void Today_differs_from_the_UTC_date_just_after_local_midnight()
    {
        // 23:30 UTC on 31 January is 00:30 on 1 February in Brussels (CET, UTC+1).
        var clock = CreateClock("2024-01-31T23:30:00Z", out _);

        clock.Today.ShouldBe(new DateOnly(2024, 2, 1));
        clock.CurrentMonth.ShouldBe(new DateOnly(2024, 2, 1));
    }

    [Fact]
    public void Now_uses_the_summer_offset_during_DST()
    {
        var clock = CreateClock("2024-07-15T10:00:00Z", out _);

        clock.Now.Offset.ShouldBe(TimeSpan.FromHours(2));
        clock.Now.Hour.ShouldBe(12);
    }

    [Fact]
    public void Now_uses_the_winter_offset_outside_DST()
    {
        var clock = CreateClock("2024-01-15T10:00:00Z", out _);

        clock.Now.Offset.ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Now_follows_the_spring_forward_transition()
    {
        // 2024-03-31 01:00 UTC is the instant Brussels jumps from 02:00 to 03:00.
        var clock = CreateClock("2024-03-31T00:59:00Z", out var time);
        clock.Now.Offset.ShouldBe(TimeSpan.FromHours(1));

        time.Advance(TimeSpan.FromMinutes(1));

        clock.Now.Offset.ShouldBe(TimeSpan.FromHours(2));
        clock.Now.Hour.ShouldBe(3);
    }

    [Theory]
    [InlineData("Europe/Brussels", true)]
    [InlineData("UTC", true)]
    [InlineData("Not/AZone", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryResolveZone_recognises_zone_ids(string? id, bool expected)
    {
        AppClock.TryResolveZone(id, out var zone).ShouldBe(expected);
        (zone is not null).ShouldBe(expected);
    }
}
