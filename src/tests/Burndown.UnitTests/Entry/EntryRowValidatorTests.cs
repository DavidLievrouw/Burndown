using Burndown.Common;
using Burndown.Entry;
using Shouldly;
using static Burndown.UnitTests.Entry.EntryTestData;

namespace Burndown.UnitTests.Entry;

public class EntryRowValidatorTests
{
    private static readonly DateOnly Today = new(2026, 5, 15);

    private static ValidRow Valid(SubmitRow row)
    {
        var result = Validator().Validate(row, Today);
        result.IsSuccess.ShouldBeTrue(result.IsSuccess ? null : string.Join(';', result.Error.Fields!.Keys));
        return result.Value;
    }

    private static IReadOnlyDictionary<string, string[]> Invalid(SubmitRow row)
    {
        var result = Validator().Validate(row, Today);
        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.Validation);
        return result.Error.Fields!;
    }

    [Fact]
    public void Accepts_an_expense_and_sends_it_from_the_account_to_cash()
    {
        var row = Valid(Expense(description: "  Bakery  ", category: "leven", tag: "SHAREDDAVID"));

        row.Type.ShouldBe(RowTypes.Expense);
        row.Description.ShouldBe("Bakery");
        row.Source.Value.ShouldBe(Checking);
        row.Destination.Value.ShouldBe(CashId);
        row.Category.ShouldBe("Leven");
        row.Tag.ShouldBe("SharedDavid");
        row.Budget!.Value.Value.ShouldBe(13);
        row.Local.ShouldBe(new DateTime(2026, 5, 15, 9, 30, 0, DateTimeKind.Unspecified));
        row.ChosenAccounts.ShouldBe([Checking]);
    }

    [Fact]
    public void Accepts_an_expense_without_a_budget()
    {
        Valid(Expense(budgetId: null)).Budget.ShouldBeNull();
    }

    [Fact]
    public void Sends_an_income_from_cash_to_the_account()
    {
        var row = Valid(Income());

        row.Source.Value.ShouldBe(CashId);
        row.Destination.Value.ShouldBe(Checking);
        row.Category.ShouldBeNull();
        row.Tag.ShouldBeNull();
    }

    [Fact]
    public void Sends_a_transfer_between_two_accounts()
    {
        var row = Valid(Transfer());

        row.Source.Value.ShouldBe(Checking);
        row.Destination.Value.ShouldBe(Savings);
        row.ChosenAccounts.ShouldBe([Checking, Savings]);
    }

    [Fact]
    public void Ignores_fields_that_do_not_belong_to_the_type()
    {
        var row = Income() with { Category = "nonsense", Tag = "nonsense", BudgetId = 999, FromAccountId = -4 };

        Valid(row).Category.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("refund")]
    public void Rejects_an_unknown_type(string? type)
    {
        Invalid(Expense() with { Type = type! }).ShouldContainKey("type");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-05-15")]
    [InlineData("2026-05-15 09:30")]
    [InlineData("2026-05-15T09:30:00")]
    [InlineData("2026-13-15T09:30")]
    [InlineData("2026-02-30T09:30")]
    public void Rejects_a_malformed_date_time(string? dateTime)
    {
        Invalid(Expense(dateTime: dateTime!)).ShouldContainKey("dateTime");
    }

    [Theory]
    [InlineData("2026-03-15T00:00", true)]
    [InlineData("2026-03-14T23:59", false)]
    [InlineData("2026-05-16T23:59", true)]
    [InlineData("2026-05-17T00:00", false)]
    public void Limits_the_date_to_the_backdated_months_and_tomorrow(string dateTime, bool allowed)
    {
        var result = Validator().Validate(Expense(dateTime: dateTime), Today);

        result.IsSuccess.ShouldBe(allowed);
    }

    [Fact]
    public void Resolves_the_backdated_limit_at_the_end_of_a_short_month()
    {
        // 2026-05-31 minus two months is 2026-03-31, and 2026-04-30 minus two months is 2026-02-28.
        Validator()
            .Validate(Expense(dateTime: "2026-03-31T00:00"), new DateOnly(2026, 5, 31))
            .IsSuccess.ShouldBeTrue();
        Validator()
            .Validate(Expense(dateTime: "2026-03-30T23:59"), new DateOnly(2026, 5, 31))
            .IsSuccess.ShouldBeFalse();
        Validator().Validate(Expense(dateTime: "2026-02-28T00:00"), new DateOnly(2026, 4, 30)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Requires_a_description(string? description)
    {
        Invalid(Expense(description: description!)).ShouldContainKey("description");
    }

    [Fact]
    public void Limits_the_description_length_after_trimming()
    {
        Validator().Validate(Expense(description: " " + new string('a', 1000) + " "), Today).IsSuccess.ShouldBeTrue();
        Invalid(Expense(description: new string('a', 1001))).ShouldContainKey("description");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.001")]
    [InlineData("1.005")]
    [InlineData("1000000000")]
    public void Rejects_an_invalid_amount(string? amount)
    {
        var parsed = amount is null
            ? (decimal?)null
            : decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);

        Invalid(Expense(amount: parsed)).ShouldContainKey("amount");
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(12.5)]
    [InlineData(999999999.99)]
    public void Accepts_amounts_at_the_boundaries(double amount)
    {
        Valid(Expense(amount: (decimal)amount)).Amount.ShouldBe((decimal)amount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(CashId)]
    public void Rejects_an_invalid_expense_account(int? accountId)
    {
        Invalid(Expense(accountId: accountId)).ShouldContainKey("accountId");
    }

    [Fact]
    public void Rejects_the_cash_account_on_income()
    {
        Invalid(Income(accountId: CashId)).ShouldContainKey("accountId");
    }

    [Fact]
    public void Requires_a_configured_category_and_target_tag_and_budget_for_an_expense()
    {
        var errors = Invalid(Expense(category: "Other", tag: null, budgetId: 99));

        errors.ShouldContainKey("category");
        errors.ShouldContainKey("tag");
        errors.ShouldContainKey("budgetId");
    }

    [Fact]
    public void Requires_different_accounts_for_a_transfer()
    {
        Invalid(Transfer(from: Checking, to: Checking)).ShouldContainKey("toAccountId");
        Invalid(Transfer(from: null, to: Savings)).ShouldContainKey("fromAccountId");
        Invalid(Transfer(from: Checking, to: CashId)).ShouldContainKey("toAccountId");
    }

    [Fact]
    public void Reports_every_invalid_field_at_once()
    {
        var errors = Invalid(Expense(dateTime: "x", description: "", amount: 0, accountId: null));

        errors.Keys.ShouldBe(["dateTime", "description", "amount", "accountId"], ignoreOrder: true);
    }
}
