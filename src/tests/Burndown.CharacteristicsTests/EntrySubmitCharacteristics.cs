using System.Globalization;
using Burndown.CharacteristicsTests.Infrastructure;
using Burndown.Common;
using Burndown.Entry;
using Burndown.Firefly;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Burndown.CharacteristicsTests;

/// <summary>
/// Characterises the Entry submit flow against the throwaway Firefly container. Every write goes through
/// <see cref="FireflyFixture.CreateClient"/>, which refuses anything but the container (PLAN.md, "Write safety").
/// </summary>
[Collection(FireflyCollection.Name)]
[Trait("Category", "Characteristics")]
public class EntrySubmitCharacteristics
{
    private static readonly TimeZoneInfo Brussels = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    private readonly FireflyFixture _firefly;

    public EntrySubmitCharacteristics(FireflyFixture firefly)
    {
        _firefly = firefly;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private EntrySubmitService CreateService(IFireflyClient client)
    {
        var options = new EntryOptions
        {
            CashAccountId = _firefly.CashId.Value,
            MaxBackdatedMonths = 2,
            Categories = ["Nodig", "Leven"],
            TargetTags = ["SharedDavid", "SharedTina"],
            Budgets =
            [
                new BudgetOption { Id = _firefly.DavidBudgetId.Value, Name = "David" },
                new BudgetOption { Id = _firefly.TinaBudgetId.Value, Name = "Tina" },
            ],
        };
        var clock = new AppClock(
            new FakeTimeProvider(DateTimeOffset.Parse("2026-05-15T10:00:00Z", CultureInfo.InvariantCulture)),
            Brussels
        );
        return new EntrySubmitService(
            client,
            clock,
            new EntryRowValidator(Options.Create(options)),
            new EntryUsageTracker(),
            NullLogger<EntrySubmitService>.Instance
        );
    }

    private SubmitRow Expense(string dateTime, string description, int? accountId = null) =>
        new(
            RowTypes.Expense,
            dateTime,
            description,
            12.34m,
            accountId ?? _firefly.CheckingId.Value,
            null,
            null,
            "Nodig",
            "SharedDavid",
            _firefly.DavidBudgetId.Value
        );

    private static async Task<IReadOnlyList<FireflyTransaction>> ReadDayAsync(IFireflyClient client, DateOnly day)
    {
        var read = await client.GetTransactionsAsync(day, day, null, Ct);
        read.IsSuccess.ShouldBeTrue();
        return read.Value;
    }

    [Fact]
    public async Task A_mixed_batch_on_exactly_the_same_moment_is_created_in_order_with_the_right_accounts()
    {
        var client = _firefly.CreateClient();
        var rows = new[]
        {
            Expense("2026-05-12T09:30", "Entry expense"),
            new SubmitRow(
                RowTypes.Income,
                "2026-05-12T09:30",
                "Entry income",
                2000m,
                _firefly.SavingsId.Value,
                null,
                null,
                null,
                null,
                null
            ),
            new SubmitRow(
                RowTypes.Transfer,
                "2026-05-12T09:30",
                "Entry transfer",
                50m,
                null,
                _firefly.CheckingId.Value,
                _firefly.SavingsId.Value,
                null,
                null,
                null
            ),
        };

        var result = await CreateService(client).SubmitAsync(new SubmitRequest(rows), Ct);

        result.IsSuccess.ShouldBeTrue();
        var response = result.Value;
        response
            .Results.Select(r => r.Status)
            .ShouldBe([RowStatuses.Created, RowStatuses.Created, RowStatuses.Created]);
        response.Results.Select(r => r.DateTime).ShouldBe(["2026-05-12T09:30", "2026-05-12T09:30", "2026-05-12T09:30"]);
        response.Results.Select(r => r.Bumped).ShouldBe([false, false, false]);

        var stored = await ReadDayAsync(client, new DateOnly(2026, 5, 12));
        var ids = response.Results.Select(r => r.Id!.Value).ToList();
        ids.ShouldBe(ids.Order().ToList());
        var expense = stored.Single(t => t.GroupId.Value == ids[0]);
        var income = stored.Single(t => t.GroupId.Value == ids[1]);
        var transfer = stored.Single(t => t.GroupId.Value == ids[2]);

        expense.Type.ShouldBe(TransactionType.Withdrawal);
        expense.SourceId.ShouldBe(_firefly.CheckingId);
        expense.DestinationId.ShouldBe(_firefly.CashId);
        expense.Category.ShouldBe("Nodig");
        expense.Tags.ShouldBe(["SharedDavid"]);
        expense.BudgetId.ShouldBe(_firefly.DavidBudgetId);
        expense.Date.TimeOfDay.ShouldBe(new TimeSpan(9, 30, 0));
        income.Type.ShouldBe(TransactionType.Deposit);
        income.SourceId.ShouldBe(_firefly.CashId);
        income.DestinationId.ShouldBe(_firefly.SavingsId);
        income.Date.TimeOfDay.ShouldBe(new TimeSpan(9, 30, 0));
        transfer.Type.ShouldBe(TransactionType.Transfer);
        transfer.SourceId.ShouldBe(_firefly.CheckingId);
        transfer.DestinationId.ShouldBe(_firefly.SavingsId);
        transfer.Date.TimeOfDay.ShouldBe(new TimeSpan(9, 30, 0));
    }

    [Fact]
    public async Task Rows_dated_today_are_shifted_back_so_the_last_lands_at_now_and_tomorrow_is_left_alone()
    {
        var client = _firefly.CreateClient();
        // The service clock is 2026-05-15 12:00 in Brussels.
        var request = new SubmitRequest(
            [
                Expense("2026-05-15T11:59", "Entry today on time"),
                Expense("2026-05-15T12:30", "Entry today shifted"),
                Expense("2026-05-15T12:45", "Entry today last"),
                Expense("2026-05-16T00:30", "Entry tomorrow"),
            ],
            ["2026-05-15T11:56"]
        );

        var result = await CreateService(client).SubmitAsync(request, Ct);

        result.IsSuccess.ShouldBeTrue();
        var response = result.Value;
        response
            .Results.Select(r => r.Status)
            .ShouldBe([RowStatuses.Created, RowStatuses.Created, RowStatuses.Created, RowStatuses.Created]);
        response
            .Results.Select(r => r.DateTime)
            .ShouldBe(["2026-05-15T11:58", "2026-05-15T11:59", "2026-05-15T12:00", "2026-05-16T00:30"]);
        response.Results.Select(r => r.Bumped).ShouldBe([true, true, true, false]);

        var today = await ReadDayAsync(client, new DateOnly(2026, 5, 15));
        today
            .Where(t => t.Description.StartsWith("Entry today", StringComparison.Ordinal))
            .OrderBy(t => t.Date)
            .Select(t => (t.Description, t.Date.TimeOfDay))
            .ShouldBe([
                ("Entry today on time", new TimeSpan(11, 58, 0)),
                ("Entry today shifted", new TimeSpan(11, 59, 0)),
                ("Entry today last", new TimeSpan(12, 0, 0)),
            ]);
        var tomorrow = await ReadDayAsync(client, new DateOnly(2026, 5, 16));
        tomorrow.Single(t => t.Description == "Entry tomorrow").Date.TimeOfDay.ShouldBe(new TimeSpan(0, 30, 0));
    }

    [Fact]
    public async Task Rows_dated_today_with_no_room_before_now_share_the_moment_of_the_previous_row()
    {
        var client = _firefly.CreateClient();
        var request = new SubmitRequest(
            [Expense("2026-05-15T12:30", "Entry shares one"), Expense("2026-05-15T12:45", "Entry shares two")],
            ["2026-05-15T12:00"]
        );

        var result = await CreateService(client).SubmitAsync(request, Ct);

        var response = result.Value;
        response.Results.Select(r => r.Status).ShouldBe([RowStatuses.Created, RowStatuses.Created]);
        response.Results.Select(r => r.DateTime).ShouldBe(["2026-05-15T12:00", "2026-05-15T12:00"]);
        var today = await ReadDayAsync(client, new DateOnly(2026, 5, 15));
        today
            .Where(t => t.Description.StartsWith("Entry shares", StringComparison.Ordinal))
            .Select(t => t.Date.TimeOfDay)
            .ShouldBe([new TimeSpan(12, 0, 0), new TimeSpan(12, 0, 0)]);
    }

    [Fact]
    public async Task A_row_that_firefly_rejects_is_mapped_to_its_field_and_the_batch_continues()
    {
        var client = _firefly.CreateClient();
        var rows = new[]
        {
            Expense("2026-05-13T09:30", "Entry unknown account", accountId: 999_999),
            Expense("2026-05-13T09:31", "Entry after rejection"),
        };

        var result = await CreateService(client).SubmitAsync(new SubmitRequest(rows), Ct);

        var response = result.Value;
        response.Results.Select(r => r.Status).ShouldBe([RowStatuses.Rejected, RowStatuses.Created]);
        response.Results[0].Errors.ShouldNotBeNull().ShouldNotBeEmpty();
        response.Stopped.ShouldBeNull();
        (await ReadDayAsync(client, new DateOnly(2026, 5, 13)))
            .Any(t => t.Description == "Entry after rejection")
            .ShouldBeTrue();
    }

    [Fact]
    public async Task An_invalid_token_stops_the_batch_at_the_first_row_and_creates_nothing()
    {
        var client = _firefly.CreateClient(token: "not-a-token");
        var rows = new[]
        {
            Expense("2026-05-14T09:30", "Entry unauthorized"),
            Expense("2026-05-14T09:31", "Entry never"),
        };

        var result = await CreateService(client).SubmitAsync(new SubmitRequest(rows), Ct);

        var response = result.Value;
        response.Results.Select(r => r.Status).ShouldBe([RowStatuses.Failed, RowStatuses.NotAttempted]);
        response.Stopped.ShouldNotBeNull().Code.ShouldBe(FireflyErrorCodes.Unauthorized);
        var stored = await ReadDayAsync(_firefly.CreateClient(), new DateOnly(2026, 5, 14));
        stored.Any(t => t.Description.StartsWith("Entry ", StringComparison.Ordinal)).ShouldBeFalse();
    }
}

/// <summary>The guard that keeps the write tests away from the owner's real Firefly. Needs no container.</summary>
public class ThrowawayGuardTests
{
    [Theory]
    [InlineData("http://localhost:8042")]
    [InlineData("http://127.0.0.1:8042/api")]
    [InlineData("http://host.docker.internal:8042")]
    [InlineData("https://firefly.example.com")]
    [InlineData("not a url")]
    public void Refuses_the_real_firefly_and_non_local_hosts(string url)
    {
        Should.Throw<InvalidOperationException>(() => ThrowawayGuard.Ensure(url));
    }

    [Theory]
    [InlineData("http://localhost:55123")]
    [InlineData("http://127.0.0.1:32768")]
    public void Accepts_a_mapped_container_port(string url)
    {
        Should.NotThrow(() => ThrowawayGuard.Ensure(url));
    }
}
