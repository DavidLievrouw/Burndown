using Burndown.Entry;
using Burndown.Firefly;
using Burndown.Overview;
using Burndown.Web;
using Shouldly;

namespace Burndown.UnitTests.Options;

public class OptionsValidatorTests
{
    private static EntryOptions ValidEntry() =>
        new()
        {
            CashAccountId = 16,
            MaxBackdatedMonths = 2,
            Categories = ["Leven", "Nodig"],
            TargetTags = ["David", "Tina"],
            Budgets = [new BudgetOption { Id = 13, Name = "David" }, new BudgetOption { Id = 14, Name = "Tina" }],
        };

    [Fact]
    public void Entry_options_accept_valid_settings()
    {
        new EntryOptionsValidator().Validate(null, ValidEntry()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Entry_options_reject_a_non_positive_cash_account(int id)
    {
        var options = ValidEntry();
        options.CashAccountId = id;

        new EntryOptionsValidator().Validate(null, options).Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(121)]
    public void Entry_options_reject_an_out_of_range_backdating(int months)
    {
        var options = ValidEntry();
        options.MaxBackdatedMonths = months;

        new EntryOptionsValidator().Validate(null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Entry_options_reject_empty_blank_and_duplicate_lists()
    {
        var empty = ValidEntry();
        empty.Categories = [];
        var duplicate = ValidEntry();
        duplicate.TargetTags = ["David", "david"];
        var blank = ValidEntry();
        blank.Categories = ["Leven", " "];

        var validator = new EntryOptionsValidator();

        validator.Validate(null, empty).Failed.ShouldBeTrue();
        validator.Validate(null, duplicate).Failed.ShouldBeTrue();
        validator.Validate(null, blank).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Entry_options_reject_invalid_budgets()
    {
        var validator = new EntryOptionsValidator();

        var none = ValidEntry();
        none.Budgets = [];
        var badId = ValidEntry();
        badId.Budgets = [new BudgetOption { Id = 0, Name = "David" }];
        var duplicateId = ValidEntry();
        duplicateId.Budgets = [new BudgetOption { Id = 1, Name = "A" }, new BudgetOption { Id = 1, Name = "B" }];
        var noName = ValidEntry();
        noName.Budgets = [new BudgetOption { Id = 1, Name = null }];
        var duplicateName = ValidEntry();
        duplicateName.Budgets = [new BudgetOption { Id = 1, Name = "A" }, new BudgetOption { Id = 2, Name = "a" }];

        validator.Validate(null, none).Failed.ShouldBeTrue();
        validator.Validate(null, badId).Failed.ShouldBeTrue();
        validator.Validate(null, duplicateId).Failed.ShouldBeTrue();
        validator.Validate(null, noName).Failed.ShouldBeTrue();
        validator.Validate(null, duplicateName).Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("2023-12", true)]
    [InlineData("2024-02", true)]
    [InlineData("2023-13", false)]
    [InlineData("2023-1", false)]
    [InlineData("202312", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Overview_minimum_month_must_be_yyyy_MM(string? value, bool valid)
    {
        var options = new OverviewOptions
        {
            MinimumMonth = value,
            MonthlyInstallmentTag = "MonthlyInstallment",
            ExcludedDestinationAccountType = "Expense account",
        };

        new OverviewOptionsValidator().Validate(null, options).Succeeded.ShouldBe(valid);
        options.TryGetMinimumMonth(out var firstDay).ShouldBe(valid);
        if (valid)
        {
            firstDay.Day.ShouldBe(1);
        }
    }

    [Fact]
    public void Overview_options_require_the_tag_and_account_type()
    {
        var validator = new OverviewOptionsValidator();

        validator
            .Validate(
                null,
                new OverviewOptions { MinimumMonth = "2023-12", ExcludedDestinationAccountType = "Expense account" }
            )
            .Failed.ShouldBeTrue();
        validator
            .Validate(
                null,
                new OverviewOptions { MinimumMonth = "2023-12", MonthlyInstallmentTag = "MonthlyInstallment" }
            )
            .Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("http://localhost:8042", "token", true)]
    [InlineData("https://firefly.example", "token", true)]
    [InlineData("http://localhost:8042", "", false)]
    [InlineData("http://localhost:8042", null, false)]
    [InlineData("localhost:8042", "token", false)]
    [InlineData("ftp://localhost", "token", false)]
    [InlineData("http://user:pw@localhost", "token", false)]
    [InlineData(null, "token", false)]
    public void Firefly_options_need_a_url_and_a_token(string? baseUrl, string? token, bool valid)
    {
        var options = new FireflyOptions { BaseUrl = baseUrl, Token = token };

        new FireflyOptionsValidator().Validate(null, options).Succeeded.ShouldBe(valid);
    }

    [Fact]
    public void Firefly_validation_messages_never_contain_the_token()
    {
        var options = new FireflyOptions { BaseUrl = "not a url", Token = "super-secret-token" };

        var result = new FireflyOptionsValidator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldNotContain("super-secret-token");
    }

    [Theory]
    [InlineData("Europe/Brussels", true)]
    [InlineData("Nowhere/Land", false)]
    [InlineData(null, false)]
    public void Burndown_options_need_a_known_time_zone(string? zone, bool valid)
    {
        new BurndownOptionsValidator()
            .Validate(null, new BurndownOptions { TimeZone = zone })
            .Succeeded.ShouldBe(valid);
    }
}
