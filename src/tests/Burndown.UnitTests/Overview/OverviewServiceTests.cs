using System.Globalization;
using Burndown.Common;
using Burndown.Firefly;
using Burndown.Overview;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using static Burndown.UnitTests.Overview.OverviewTestData;

namespace Burndown.UnitTests.Overview;

public class OverviewServiceTests
{
    private static readonly TimeZoneInfo Brussels = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    private static OverviewService CreateService(
        FakeFireflyClient client,
        string utcInstant = "2024-02-15T10:00:00Z"
    ) =>
        new(
            client,
            new AppClock(
                new FakeTimeProvider(DateTimeOffset.Parse(utcInstant, CultureInfo.InvariantCulture)),
                Brussels
            ),
            Microsoft.Extensions.Options.Options.Create(OverviewTestData.DefaultOptions())
        );

    [Fact]
    public async Task Defaults_to_the_current_month_and_makes_exactly_two_list_calls()
    {
        var client = FakeFireflyClient.Returning(
            Deposit(2024, 1, 5, 3100m),
            Withdrawal(2024, 2, 10, 40m, "Lunch"),
            Withdrawal(2024, 1, 20, 100m, "Loan", ["MonthlyInstallment"])
        );

        var result = await CreateService(client).GetAsync(null, null, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Month.ShouldBe("2024-02");
        result.Value.CurrentMonth.ShouldBe("2024-02");
        result.Value.MinimumMonth.ShouldBe("2023-12");
        result.Value.MaximumMonth.ShouldBe("2024-03");
        result.Value.Today.ShouldBe(new DateOnly(2024, 2, 15));
        result.Value.Budget.Effective.ShouldBe(3000m);
        result.Value.Totals.Spent.ShouldBe(40m);
        client.Calls.Count.ShouldBe(2);
        client.OtherCalls.ShouldBe(0);
        client.Calls.ShouldContain((new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31), TransactionType.Deposit));
        client.Calls.ShouldContain((new DateOnly(2024, 1, 1), new DateOnly(2024, 2, 29), TransactionType.Withdrawal));
    }

    [Fact]
    public async Task Uses_the_configured_zone_for_today_around_midnight()
    {
        // 23:30 UTC on 31 January is already 1 February in Brussels.
        var client = FakeFireflyClient.Returning();

        var result = await CreateService(client, "2024-01-31T23:30:00Z")
            .GetAsync(null, null, TestContext.Current.CancellationToken);

        result.Value.Today.ShouldBe(new DateOnly(2024, 2, 1));
        result.Value.Month.ShouldBe("2024-02");
        result.Value.MaximumMonth.ShouldBe("2024-03");
        result.Value.Chart.TodayDay.ShouldBe(1);
    }

    [Theory]
    [InlineData("2023-12")]
    [InlineData("2024-02")]
    [InlineData("2024-03")]
    public async Task Accepts_months_from_the_minimum_up_to_the_month_after_the_current_one(string month)
    {
        var client = FakeFireflyClient.Returning();

        var result = await CreateService(client).GetAsync(month, null, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Month.ShouldBe(month);
    }

    [Theory]
    [InlineData("2023-11")]
    [InlineData("2024-04")]
    [InlineData("2024-13")]
    [InlineData("2024-1")]
    [InlineData("2024-02-01")]
    [InlineData("February")]
    [InlineData("")]
    public async Task Rejects_a_month_that_is_malformed_or_out_of_range_without_calling_Firefly(string month)
    {
        var client = FakeFireflyClient.Returning();

        var result = await CreateService(client).GetAsync(month, null, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.Validation);
        result.Error.Fields.ShouldNotBeNull().ShouldContainKey("month");
        client.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1234.5", 1234.5)]
    [InlineData("+10", 10)]
    public async Task Accepts_a_non_negative_invariant_budget_override(string value, double expected)
    {
        var client = FakeFireflyClient.Returning(Deposit(2024, 1, 5, 3100m));

        var result = await CreateService(client).GetAsync(null, value, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Budget.Overridden.ShouldBeTrue();
        result.Value.Budget.Effective.ShouldBe((decimal)expected);
        result.Value.Budget.Computed.ShouldBe(3100m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("12,5")]
    [InlineData("1e3")]
    [InlineData("1 000")]
    [InlineData("1000000000")]
    public async Task Rejects_an_invalid_budget_override_without_calling_Firefly(string value)
    {
        var client = FakeFireflyClient.Returning();

        var result = await CreateService(client).GetAsync(null, value, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.Validation);
        result.Error.Fields.ShouldNotBeNull().ShouldContainKey("budgetOverride");
        client.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reports_every_invalid_field_at_once()
    {
        var result = await CreateService(FakeFireflyClient.Returning())
            .GetAsync("nope", "nope", TestContext.Current.CancellationToken);

        result.Error.Fields.ShouldNotBeNull().Keys.ShouldBe(["month", "budgetOverride"], ignoreOrder: true);
    }

    [Theory]
    [InlineData(TransactionType.Deposit)]
    [InlineData(TransactionType.Withdrawal)]
    public async Task Returns_the_Firefly_failure_when_either_list_call_fails(TransactionType failing)
    {
        var failure = new Error(FireflyErrorCodes.Unavailable, "Firefly is down.");
        var client = new FakeFireflyClient(
            (_, _, type) =>
                type == failing
                    ? Result<IReadOnlyList<FireflyTransaction>>.Failure(failure)
                    : Result<IReadOnlyList<FireflyTransaction>>.Success([])
        );

        var result = await CreateService(client).GetAsync(null, null, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(failure);
        client.Calls.Count.ShouldBe(2);
    }
}
