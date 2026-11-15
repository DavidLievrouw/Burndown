using Burndown.Common;
using Burndown.Entry;
using Burndown.Firefly;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static Burndown.UnitTests.Entry.EntryTestData;

namespace Burndown.UnitTests.Entry;

public class EntrySubmitServiceTests
{
    private readonly RecordingFireflyClient _firefly = new();
    private readonly EntryUsageTracker _usage = new();

    private EntrySubmitService Create(string utc = "2026-05-15T10:00:00Z") =>
        new(_firefly, Clock(utc).Clock, Validator(), _usage, NullLogger<EntrySubmitService>.Instance);

    private async Task<SubmitResponse> SubmitAsync(params SubmitRow[] rows)
    {
        var result = await Create().SubmitAsync(new SubmitRequest(rows), TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private static Result<CreatedTransaction> Fail(string code, IReadOnlyDictionary<string, string[]>? fields = null) =>
        Result<CreatedTransaction>.Failure(new Error(code, "failed", fields));

    [Fact]
    public async Task Creates_the_rows_in_grid_order_with_the_right_payloads()
    {
        var response = await SubmitAsync(
            Expense(),
            Income(dateTime: "2026-05-15T09:31"),
            Transfer(dateTime: "2026-05-15T09:32")
        );

        response
            .Results.Select(r => r.Status)
            .ShouldBe([RowStatuses.Created, RowStatuses.Created, RowStatuses.Created]);
        response.Results.Select(r => r.Id).ShouldBe([100, 101, 102]);
        response.Summary.Created.ShouldBe(3);
        response.Stopped.ShouldBeNull();

        var expense = _firefly.Created[0];
        expense.Type.ShouldBe(TransactionType.Withdrawal);
        expense.SourceId.Value.ShouldBe(Checking);
        expense.DestinationId.Value.ShouldBe(CashId);
        expense.Category.ShouldBe("Leven");
        expense.Tags.ShouldBe(["SharedDavid"]);
        expense.BudgetId!.Value.Value.ShouldBe(13);
        expense.Amount.ShouldBe(12.5m);
        expense.Date.ShouldBe(new DateTimeOffset(2026, 5, 15, 9, 30, 0, TimeSpan.FromHours(2)));

        var income = _firefly.Created[1];
        income.Type.ShouldBe(TransactionType.Deposit);
        income.SourceId.Value.ShouldBe(CashId);
        income.DestinationId.Value.ShouldBe(Checking);
        income.Tags.ShouldBeEmpty();
        income.BudgetId.ShouldBeNull();

        var transfer = _firefly.Created[2];
        transfer.Type.ShouldBe(TransactionType.Transfer);
        transfer.SourceId.Value.ShouldBe(Checking);
        transfer.DestinationId.Value.ShouldBe(Savings);
    }

    [Fact]
    public async Task Moves_a_row_up_to_the_previous_time_of_earlier_requests_but_allows_an_equal_time()
    {
        var result = await Create()
            .SubmitAsync(
                new SubmitRequest([Expense(dateTime: "2026-05-15T09:30")], ["2026-05-15T09:30", "2026-05-14T22:00"]),
                TestContext.Current.CancellationToken
            );

        result.IsSuccess.ShouldBeTrue();
        result.Value.Results[0].Bumped.ShouldBeFalse();
        result.Value.Results[0].DateTime.ShouldBe("2026-05-15T09:30");

        var earlier = await Create()
            .SubmitAsync(
                new SubmitRequest([Expense(dateTime: "2026-05-15T09:00")], ["2026-05-15T09:30"]),
                TestContext.Current.CancellationToken
            );

        earlier.Value.Results[0].Bumped.ShouldBeTrue();
        earlier.Value.Results[0].DateTime.ShouldBe("2026-05-15T09:30");
    }

    [Fact]
    public async Task Rejects_a_previous_time_that_is_not_formatted_as_a_local_date_time()
    {
        var result = await Create()
            .SubmitAsync(new SubmitRequest([Expense()], ["yesterday"]), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        _firefly.Created.ShouldBeEmpty();
    }

    [Fact]
    public async Task Allows_rows_on_exactly_the_same_moment_and_moves_an_earlier_row_up()
    {
        var response = await SubmitAsync(
            Expense(dateTime: "2026-05-15T09:30"),
            Expense(dateTime: "2026-05-15T09:30"),
            Expense(dateTime: "2026-05-15T09:00")
        );

        response.Results.Select(r => r.Bumped).ShouldBe([false, false, true]);
        response.Results.Select(r => r.DateTime).ShouldBe(["2026-05-15T09:30", "2026-05-15T09:30", "2026-05-15T09:30"]);
        _firefly.Created.Select(c => c.Date).Distinct().ShouldHaveSingleItem();
        _firefly.Created[1].Date.ShouldBe(new DateTimeOffset(2026, 5, 15, 9, 30, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public async Task Shifts_the_rows_dated_today_back_so_the_last_lands_at_now_and_leaves_tomorrow_alone()
    {
        var response = await SubmitAsync(
            Expense(dateTime: "2026-05-15T11:59"),
            Expense(dateTime: "2026-05-15T12:30"),
            Expense(dateTime: "2026-05-15T12:31"),
            Expense(dateTime: "2026-05-16T00:05")
        );

        response
            .Results.Select(r => r.Status)
            .ShouldBe([RowStatuses.Created, RowStatuses.Created, RowStatuses.Created, RowStatuses.Created]);
        response
            .Results.Select(r => r.DateTime)
            .ShouldBe(["2026-05-15T11:58", "2026-05-15T11:59", "2026-05-15T12:00", "2026-05-16T00:05"]);
        response.Results.Select(r => r.Bumped).ShouldBe([true, true, true, false]);
        _firefly.Created[0].Date.ShouldBe(new DateTimeOffset(2026, 5, 15, 11, 58, 0, TimeSpan.FromHours(2)));
        _firefly.Created[2].Date.ShouldBe(new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.FromHours(2)));
        _firefly.Created[3].Date.ShouldBe(new DateTimeOffset(2026, 5, 16, 0, 5, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public async Task Never_shifts_rows_dated_today_before_the_previous_row()
    {
        var request = new SubmitRequest(
            [
                Expense(dateTime: "2026-05-15T12:20"),
                Expense(dateTime: "2026-05-15T12:21"),
                Expense(dateTime: "2026-05-15T12:22"),
            ],
            ["2026-05-15T11:58"]
        );

        var result = await Create().SubmitAsync(request, TestContext.Current.CancellationToken);

        var response = result.Value;
        response
            .Results.Select(r => r.Status)
            .ShouldBe([RowStatuses.Created, RowStatuses.Created, RowStatuses.Created]);
        response.Results.Select(r => r.DateTime).ShouldBe(["2026-05-15T11:58", "2026-05-15T11:59", "2026-05-15T12:00"]);
        _firefly.Created.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Reports_invalid_rows_without_sending_them_and_continues_with_the_rest()
    {
        var response = await SubmitAsync(Expense(), Expense(amount: 0), Expense(dateTime: "2026-05-15T09:31"));

        response
            .Results.Select(r => r.Status)
            .ShouldBe([RowStatuses.Created, RowStatuses.Invalid, RowStatuses.Created]);
        response.Results[1].Errors!.ShouldContainKey("amount");
        response.Results[1].Id.ShouldBeNull();
        response.Summary.Invalid.ShouldBe(1);
        _firefly.Created.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_null_row_is_invalid()
    {
        var result = await Create().SubmitAsync(new SubmitRequest([null!]), TestContext.Current.CancellationToken);

        result.Value.Results[0].Status.ShouldBe(RowStatuses.Invalid);
        _firefly.Created.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_invalid_row_does_not_take_part_in_the_time_sequence()
    {
        var response = await SubmitAsync(
            Expense(dateTime: "2026-05-15T09:30", amount: 0),
            Expense(dateTime: "2026-05-15T09:30")
        );

        response.Results[1].Bumped.ShouldBeFalse();
        response.Results[1].DateTime.ShouldBe("2026-05-15T09:30");
    }

    [Fact]
    public async Task Maps_a_firefly_rejection_to_the_row_fields_and_continues()
    {
        _firefly.CreateScript = (index, _) =>
            index == 1
                ? Fail(
                    FireflyErrorCodes.Validation,
                    new Dictionary<string, string[]>
                    {
                        ["source_id"] = ["The source is invalid."],
                        ["destination_name"] = ["The destination is invalid."],
                        ["category_name"] = ["The category is invalid."],
                        ["tags.0"] = ["The tag is invalid."],
                        ["budget_id"] = ["The budget is invalid."],
                        ["date"] = ["The date is invalid."],
                        ["amount"] = ["The amount is invalid."],
                        ["description"] = ["The description is invalid."],
                        ["transaction_journal_id"] = ["Something else."],
                    }
                )
                : null;

        var response = await SubmitAsync(
            Expense(),
            Expense(dateTime: "2026-05-15T09:31"),
            Expense(dateTime: "2026-05-15T09:32")
        );

        response
            .Results.Select(r => r.Status)
            .ShouldBe([RowStatuses.Created, RowStatuses.Rejected, RowStatuses.Created]);
        var errors = response.Results[1].Errors!;
        errors["accountId"].ShouldBe(["The source is invalid.", "The destination is invalid."]);
        errors["category"].ShouldBe(["The category is invalid."]);
        errors["tag"].ShouldBe(["The tag is invalid."]);
        errors["budgetId"].ShouldBe(["The budget is invalid."]);
        errors["dateTime"].ShouldBe(["The date is invalid."]);
        errors["amount"].ShouldBe(["The amount is invalid."]);
        errors["description"].ShouldBe(["The description is invalid."]);
        errors["row"].ShouldBe(["Something else."]);
        response.Stopped.ShouldBeNull();
        response.Summary.Rejected.ShouldBe(1);
    }

    [Fact]
    public async Task Maps_the_source_and_destination_of_a_transfer_to_their_own_fields()
    {
        _firefly.CreateScript = (_, _) =>
            Fail(
                FireflyErrorCodes.Validation,
                new Dictionary<string, string[]> { ["source_id"] = ["a"], ["destination_id"] = ["b"] }
            );

        var response = await SubmitAsync(Transfer());

        response.Results[0].Errors!["fromAccountId"].ShouldBe(["a"]);
        response.Results[0].Errors!["toAccountId"].ShouldBe(["b"]);
    }

    [Fact]
    public async Task Maps_the_destination_of_an_income_to_the_account_field()
    {
        _firefly.CreateScript = (_, _) =>
            Fail(FireflyErrorCodes.Validation, new Dictionary<string, string[]> { ["destination_id"] = ["b"] });

        var response = await SubmitAsync(Income());

        response.Results[0].Errors!["accountId"].ShouldBe(["b"]);
    }

    [Theory]
    [InlineData(FireflyErrorCodes.Unauthorized, RowStatuses.Failed)]
    [InlineData(FireflyErrorCodes.Forbidden, RowStatuses.Failed)]
    [InlineData(FireflyErrorCodes.Unavailable, RowStatuses.Failed)]
    [InlineData(FireflyErrorCodes.Unexpected, RowStatuses.Failed)]
    [InlineData(FireflyErrorCodes.Timeout, RowStatuses.Uncertain)]
    [InlineData(FireflyErrorCodes.InvalidResponse, RowStatuses.Uncertain)]
    public async Task Stops_the_batch_on_an_infrastructure_failure_and_never_sends_the_rest(string code, string status)
    {
        _firefly.CreateScript = (index, _) => index == 1 ? Fail(code) : null;

        var response = await SubmitAsync(
            Expense(),
            Expense(dateTime: "2026-05-15T09:31"),
            Expense(dateTime: "2026-05-15T09:32"),
            Expense(dateTime: "2026-05-15T09:33", amount: 0)
        );

        response
            .Results.Select(r => r.Status)
            .ShouldBe([RowStatuses.Created, status, RowStatuses.NotAttempted, RowStatuses.Invalid]);
        response.Stopped.ShouldNotBeNull();
        response.Stopped.AtRow.ShouldBe(1);
        response.Stopped.Code.ShouldBe(code);
        _firefly.Created.Count.ShouldBe(2);
        response.Results[2].DateTime.ShouldBe("2026-05-15T09:32");
    }

    [Fact]
    public async Task Never_resends_a_row_that_failed()
    {
        _firefly.CreateScript = (_, _) => Fail(FireflyErrorCodes.Timeout);

        await SubmitAsync(Expense());

        _firefly.Created.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Cancellation_propagates_and_sends_nothing_more()
    {
        using var cts = new CancellationTokenSource();
        _firefly.CreateScript = (index, _) =>
        {
            if (index == 0)
            {
                cts.Cancel();
            }

            return null;
        };

        await Should.ThrowAsync<OperationCanceledException>(() =>
            Create().SubmitAsync(new SubmitRequest([Expense(), Expense(dateTime: "2026-05-15T09:31")]), cts.Token)
        );

        _firefly.Created.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Rejects_an_empty_or_missing_batch()
    {
        foreach (var request in new SubmitRequest?[] { null, new SubmitRequest(null), new SubmitRequest([]) })
        {
            var result = await Create().SubmitAsync(request, TestContext.Current.CancellationToken);

            result.IsSuccess.ShouldBeFalse();
            result.Error.Code.ShouldBe(ErrorCodes.Validation);
            result.Error.Fields!.ShouldContainKey("rows");
        }

        _firefly.Created.ShouldBeEmpty();
    }

    [Fact]
    public async Task Limits_the_batch_size()
    {
        var atLimit = Enumerable
            .Range(0, EntrySubmitService.MaxBatchSize)
            .Select(i => Expense(dateTime: $"2026-05-15T{i / 60:00}:{i % 60:00}"));
        var overLimit = atLimit.Append(Expense(dateTime: "2026-05-15T23:00"));

        var ok = await Create().SubmitAsync(new SubmitRequest([.. atLimit]), TestContext.Current.CancellationToken);
        var tooMany = await Create()
            .SubmitAsync(new SubmitRequest([.. overLimit]), TestContext.Current.CancellationToken);

        ok.IsSuccess.ShouldBeTrue();
        tooMany.IsSuccess.ShouldBeFalse();
        tooMany.Error.Fields!.ShouldContainKey("rows");
        _firefly.Created.Count.ShouldBe(EntrySubmitService.MaxBatchSize);
    }

    [Fact]
    public async Task Uses_today_in_the_configured_zone_for_the_date_range()
    {
        // 2026-05-15 23:30 UTC is already 2026-05-16 in Brussels, so the latest allowed date is 2026-05-17.
        var result = await Create("2026-05-15T23:30:00Z")
            .SubmitAsync(
                new SubmitRequest([Expense(dateTime: "2026-05-17T08:00")]),
                TestContext.Current.CancellationToken
            );

        result.Value.Results[0].Status.ShouldBe(RowStatuses.Created);
    }

    [Fact]
    public async Task Records_the_used_values_only_for_created_rows()
    {
        _firefly.CreateScript = (index, _) => index == 1 ? Fail(FireflyErrorCodes.Validation) : null;

        await SubmitAsync(
            Expense(accountId: Savings, category: "Nodig", tag: "Settled", budgetId: 14),
            Expense(
                dateTime: "2026-05-15T09:31",
                accountId: Checking,
                category: "Optioneel",
                tag: "SharedTina",
                budgetId: 13
            )
        );

        _usage
            .Rank([new AccountItem(Checking, "Checking"), new AccountItem(Savings, "Savings")])[0]
            .Id.ShouldBe(Savings);
        _usage.RankCategories(["Leven", "Nodig", "Optioneel"])[0].ShouldBe("Nodig");
        _usage.RankTags(["SharedDavid", "SharedTina", "Settled"])[0].ShouldBe("Settled");
        _usage.Rank([new BudgetItem(13, "David"), new BudgetItem(14, "Tina")])[0].Id.ShouldBe(14);
    }
}
