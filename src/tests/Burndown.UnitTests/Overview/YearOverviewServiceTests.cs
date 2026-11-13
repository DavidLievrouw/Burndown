using System.Globalization;
using Burndown.Common;
using Burndown.Firefly;
using Burndown.Overview;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using static Burndown.UnitTests.Overview.OverviewTestData;

namespace Burndown.UnitTests.Overview;

public class YearOverviewServiceTests
{
    private static readonly TimeZoneInfo Brussels = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    private static YearOverviewService CreateService(FakeFireflyClient client) =>
        new(
            client,
            new AppClock(
                new FakeTimeProvider(DateTimeOffset.Parse("2024-02-15T10:00:00Z", CultureInfo.InvariantCulture)),
                Brussels
            ),
            Microsoft.Extensions.Options.Options.Create(DefaultOptions())
        );

    private static FakeFireflyClient Data() =>
        FakeFireflyClient.Returning(
            Deposit(2023, 11, 3, 3000m),
            Withdrawal(2023, 12, 10, 1000m, "December"),
            Deposit(2023, 12, 5, 3100m),
            Withdrawal(2023, 12, 20, 100m, "Loan", ["MonthlyInstallment"]),
            Withdrawal(2024, 1, 10, 500m, "January"),
            Deposit(2024, 1, 5, 2000m),
            Withdrawal(2024, 2, 3, 2200m, "February")
        );

    [Fact]
    public async Task Lists_the_months_of_the_year_before_the_viewed_month_with_one_pair_of_queries()
    {
        var client = Data();

        var result = await CreateService(client).GetAsync("2024-02", TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Year.ShouldBe(2024);
        result.Value.Months.ShouldBe([new MonthResult("2024-01", 2500m)]);
        result.Value.Total.ShouldBe(2500m);
        client.Calls.Count.ShouldBe(2);
        client.Calls.ShouldContain((new DateOnly(2023, 12, 1), new DateOnly(2023, 12, 31), TransactionType.Deposit));
        client.Calls.ShouldContain((new DateOnly(2023, 12, 1), new DateOnly(2024, 1, 31), TransactionType.Withdrawal));
    }

    [Fact]
    public async Task Sums_several_months_including_an_overspent_one()
    {
        var result = await CreateService(Data()).GetAsync("2024-03", TestContext.Current.CancellationToken);

        // January: 3000 - 500 = 2500. February: budget 2000 (January income, no installments) - 2200 = -200.
        result.Value.Months.ShouldBe([new MonthResult("2024-01", 2500m), new MonthResult("2024-02", -200m)]);
        result.Value.Total.ShouldBe(2300m);
    }

    [Fact]
    public async Task Shows_the_previous_year_when_january_is_viewed()
    {
        var client = Data();

        var result = await CreateService(client).GetAsync("2024-01", TestContext.Current.CancellationToken);

        // The minimum month is 2023-12, so only December 2023 exists: 3000 (November income) - 1000.
        result.Value.Year.ShouldBe(2023);
        result.Value.Months.ShouldBe([new MonthResult("2023-12", 2000m)]);
        result.Value.Total.ShouldBe(2000m);
    }

    [Fact]
    public async Task Has_no_previous_data_in_the_minimum_month_and_does_not_call_Firefly()
    {
        var client = Data();

        var result = await CreateService(client).GetAsync("2023-12", TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Months.ShouldBeEmpty();
        result.Value.Total.ShouldBe(0m);
        client.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Defaults_to_the_current_month()
    {
        var result = await CreateService(Data()).GetAsync(null, TestContext.Current.CancellationToken);

        result.Value.Month.ShouldBe("2024-02");
        result.Value.Months.Select(m => m.Month).ShouldBe(["2024-01"]);
    }

    [Theory]
    [InlineData("2023-11")]
    [InlineData("2024-04")]
    [InlineData("garbage")]
    public async Task Rejects_a_month_outside_the_allowed_range(string month)
    {
        var result = await CreateService(Data()).GetAsync(month, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.Validation);
        result.Error.Fields!.ShouldContainKey("month");
    }

    [Fact]
    public async Task Passes_a_Firefly_failure_through()
    {
        var client = new FakeFireflyClient(
            (_, _, _) =>
                Result<IReadOnlyList<FireflyTransaction>>.Failure(new Error(FireflyErrorCodes.Timeout, "Too slow."))
        );

        var result = await CreateService(client).GetAsync("2024-02", TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(FireflyErrorCodes.Timeout);
    }
}
