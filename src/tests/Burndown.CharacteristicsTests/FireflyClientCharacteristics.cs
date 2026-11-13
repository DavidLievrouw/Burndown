using Burndown.CharacteristicsTests.Infrastructure;
using Burndown.Common;
using Burndown.Firefly;
using Shouldly;

namespace Burndown.CharacteristicsTests;

/// <summary>Characterises the real Firefly III API (pinned image) as seen through <see cref="FireflyClient"/>.</summary>
[Collection(FireflyCollection.Name)]
[Trait("Category", "Characteristics")]
public class FireflyClientCharacteristics
{
    private static readonly TimeZoneInfo Brussels = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    private readonly FireflyFixture _firefly;

    public FireflyClientCharacteristics(FireflyFixture firefly)
    {
        _firefly = firefly;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset At(int month, int day, int hour, int minute)
    {
        var local = new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Brussels.GetUtcOffset(local));
    }

    private NewTransaction Expense(DateTimeOffset date, string description, decimal amount = 10m) =>
        new(
            TransactionType.Withdrawal,
            date,
            amount,
            description,
            _firefly.CheckingId,
            _firefly.CashId,
            "Nodig",
            null,
            ["SharedDavid"]
        );

    [Fact]
    public async Task About_reports_the_pinned_version()
    {
        var result = await _firefly.CreateClient().GetAboutAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe("6.6.6");
    }

    [Fact]
    public async Task A_bad_token_is_reported_as_unauthorized_on_every_call()
    {
        var client = _firefly.CreateClient(token: "not-a-token");

        (await client.GetAboutAsync(Ct)).Error.Code.ShouldBe(FireflyErrorCodes.Unauthorized);
        (await client.GetAccountsAsync(AccountKind.Asset, Ct)).Error.Code.ShouldBe(FireflyErrorCodes.Unauthorized);
        (await client.CreateTransactionAsync(Expense(At(2, 1, 8, 0), "Unauthorized"), Ct)).Error.Code.ShouldBe(
            FireflyErrorCodes.Unauthorized
        );
    }

    [Fact]
    public async Task Accounts_are_listed_by_kind_across_pages()
    {
        var client = _firefly.CreateClient(pageSize: 1);

        var assets = await client.GetAccountsAsync(AccountKind.Asset, Ct);
        var cash = await client.GetAccountsAsync(AccountKind.Cash, Ct);

        assets.Value.Select(a => a.Name).ShouldContain("Checking");
        assets.Value.Select(a => a.Name).ShouldContain("Savings");
        assets.Value.ShouldAllBe(a => a.Kind == AccountKind.Asset);
        assets.Value.Select(a => a.Id).Distinct().Count().ShouldBe(assets.Value.Count);
        cash.Value.Select(a => a.Id).ShouldContain(_firefly.CashId);
    }

    [Fact]
    public async Task Budgets_are_listed_across_pages()
    {
        var result = await _firefly.CreateClient(pageSize: 1).GetBudgetsAsync(Ct);

        result.Value.Select(b => b.Id).ShouldBe([_firefly.DavidBudgetId, _firefly.TinaBudgetId], ignoreOrder: true);
        result.Value.ShouldAllBe(b => b.Active);
    }

    [Fact]
    public async Task A_created_expense_can_be_read_back_with_every_field()
    {
        var client = _firefly.CreateClient();
        var date = At(2, 3, 10, 9);
        var transaction = Expense(date, "Readback bakery", 12.5m) with { BudgetId = _firefly.DavidBudgetId };

        var created = await client.CreateTransactionAsync(transaction, Ct);
        var read = await client.GetTransactionsAsync(
            new DateOnly(2026, 2, 3),
            new DateOnly(2026, 2, 3),
            TransactionType.Withdrawal,
            Ct
        );

        created.IsSuccess.ShouldBeTrue();
        var split = read.Value.Single(t => t.GroupId == created.Value.Id);
        split.Type.ShouldBe(TransactionType.Withdrawal);
        split.Date.ShouldBe(date);
        split.Amount.ShouldBe(12.5m);
        split.Description.ShouldBe("Readback bakery");
        split.SourceId.ShouldBe(_firefly.CheckingId);
        split.SourceType.ShouldBe("Asset account");
        split.DestinationId.ShouldBe(_firefly.CashId);
        split.DestinationType.ShouldBe("Cash account");
        split.Category.ShouldBe("Nodig");
        split.BudgetId.ShouldBe(_firefly.DavidBudgetId);
        split.Tags.ShouldBe(["SharedDavid"]);
    }

    [Fact]
    public async Task Income_from_the_cash_account_into_an_asset_account_is_accepted()
    {
        var client = _firefly.CreateClient();
        var income = new NewTransaction(
            TransactionType.Deposit,
            At(2, 4, 11, 0),
            100m,
            "Readback income",
            _firefly.CashId,
            _firefly.SavingsId,
            null,
            null,
            []
        );

        var created = await client.CreateTransactionAsync(income, Ct);
        var read = await client.GetTransactionsAsync(
            new DateOnly(2026, 2, 4),
            new DateOnly(2026, 2, 4),
            TransactionType.Deposit,
            Ct
        );

        created.IsSuccess.ShouldBeTrue(created.IsSuccess ? null : created.Error.Message);
        var split = read.Value.Single(t => t.GroupId == created.Value.Id);
        split.Type.ShouldBe(TransactionType.Deposit);
        split.SourceId.ShouldBe(_firefly.CashId);
        split.DestinationId.ShouldBe(_firefly.SavingsId);
        split.Category.ShouldBeNull();
        split.BudgetId.ShouldBeNull();
        split.Tags.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_transfer_between_two_asset_accounts_is_accepted()
    {
        var client = _firefly.CreateClient();
        var transfer = new NewTransaction(
            TransactionType.Transfer,
            At(2, 5, 12, 0),
            25m,
            "Readback transfer",
            _firefly.CheckingId,
            _firefly.SavingsId,
            null,
            null,
            []
        );

        var created = await client.CreateTransactionAsync(transfer, Ct);
        var read = await client.GetTransactionsAsync(
            new DateOnly(2026, 2, 5),
            new DateOnly(2026, 2, 5),
            TransactionType.Transfer,
            Ct
        );

        created.IsSuccess.ShouldBeTrue(created.IsSuccess ? null : created.Error.Message);
        var split = read.Value.Single(t => t.GroupId == created.Value.Id);
        split.SourceId.ShouldBe(_firefly.CheckingId);
        split.DestinationId.ShouldBe(_firefly.SavingsId);
    }

    [Fact]
    public async Task A_transfer_to_the_same_account_is_a_validation_error_with_fields()
    {
        var transfer = new NewTransaction(
            TransactionType.Transfer,
            At(2, 6, 12, 0),
            25m,
            "Same account",
            _firefly.CheckingId,
            _firefly.CheckingId,
            null,
            null,
            []
        );

        var result = await _firefly.CreateClient().CreateTransactionAsync(transfer, Ct);

        result.Error.Code.ShouldBe(FireflyErrorCodes.Validation);
        var fields = result.Error.Fields.ShouldNotBeNull();
        fields.ShouldNotBeEmpty();
        fields.Keys.ShouldAllBe(k => !k.StartsWith("transactions.", StringComparison.Ordinal));
        fields.Values.ShouldAllBe(messages => messages.Length > 0);
    }

    [Fact]
    public async Task An_empty_description_is_reported_on_the_description_field()
    {
        var result = await _firefly.CreateClient().CreateTransactionAsync(Expense(At(2, 7, 8, 0), string.Empty), Ct);

        result.Error.Code.ShouldBe(FireflyErrorCodes.Validation);
        result.Error.Fields.ShouldNotBeNull().Keys.ShouldContain("description");
    }

    [Fact]
    public async Task A_description_of_1001_characters_is_rejected_and_1000_is_accepted()
    {
        var client = _firefly.CreateClient();

        var tooLong = await client.CreateTransactionAsync(Expense(At(2, 8, 8, 0), new string('x', 1001)), Ct);
        var maximum = await client.CreateTransactionAsync(Expense(At(2, 8, 8, 1), new string('y', 1000)), Ct);

        tooLong.Error.Code.ShouldBe(FireflyErrorCodes.Validation);
        tooLong.Error.Fields.ShouldNotBeNull().Keys.ShouldContain("description");
        maximum.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task An_unknown_budget_is_a_validation_error()
    {
        var transaction = Expense(At(2, 9, 8, 0), "Unknown budget") with { BudgetId = BudgetId.From(999_999) };

        var result = await _firefly.CreateClient().CreateTransactionAsync(transaction, Ct);

        result.Error.Code.ShouldBe(FireflyErrorCodes.Validation);
        result.Error.Fields.ShouldNotBeNull().Keys.ShouldContain("budget_id");
    }

    [Fact]
    public async Task A_withdrawal_from_a_non_existent_account_is_a_validation_error()
    {
        var transaction = Expense(At(2, 10, 8, 0), "Unknown source") with { SourceId = AccountId.From(999_999) };

        var result = await _firefly.CreateClient().CreateTransactionAsync(transaction, Ct);

        result.Error.Code.ShouldBe(FireflyErrorCodes.Validation);
        result.Error.Fields.ShouldNotBeNull().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Transactions_span_pages_and_respect_the_date_range_and_type_filter()
    {
        var client = _firefly.CreateClient(pageSize: 2);
        for (var minute = 0; minute < 5; minute++)
        {
            (
                await client.CreateTransactionAsync(Expense(At(4, 15, 10, minute), $"Paged {minute}"), Ct)
            ).IsSuccess.ShouldBeTrue();
        }

        (await client.CreateTransactionAsync(Expense(At(4, 14, 23, 59), "Day before"), Ct)).IsSuccess.ShouldBeTrue();
        (await client.CreateTransactionAsync(Expense(At(4, 16, 0, 0), "Day after"), Ct)).IsSuccess.ShouldBeTrue();

        var result = await client.GetTransactionsAsync(
            new DateOnly(2026, 4, 15),
            new DateOnly(2026, 4, 15),
            TransactionType.Withdrawal,
            Ct
        );

        result
            .Value.Select(t => t.Description)
            .Order()
            .ShouldBe(["Paged 0", "Paged 1", "Paged 2", "Paged 3", "Paged 4"]);
        result.Value.Select(t => t.Date).Order().ShouldBe(Enumerable.Range(0, 5).Select(m => At(4, 15, 10, m)));
    }

    [Fact]
    public async Task Categories_and_tags_created_with_a_transaction_are_listed()
    {
        var client = _firefly.CreateClient(pageSize: 1);
        (
            await client.CreateTransactionAsync(
                Expense(At(2, 11, 8, 0), "Tag source") with
                {
                    Category = "Optioneel",
                    Tags = ["SharedTina"],
                },
                Ct
            )
        ).IsSuccess.ShouldBeTrue();

        var categories = await client.GetCategoriesAsync(Ct);
        var tags = await client.GetTagsAsync(Ct);

        categories.Value.ShouldContain("Optioneel");
        tags.Value.ShouldContain("SharedTina");
    }

    [Fact]
    public async Task Description_suggestions_find_earlier_descriptions_by_any_part()
    {
        var client = _firefly.CreateClient();
        (
            await client.CreateTransactionAsync(Expense(At(2, 12, 8, 0), "Suggestible Hardware Store"), Ct)
        ).IsSuccess.ShouldBeTrue();

        var result = await client.SuggestDescriptionsAsync("suggestible", 10, Ct);

        result.Value.ShouldContain("Suggestible Hardware Store");
    }
}
