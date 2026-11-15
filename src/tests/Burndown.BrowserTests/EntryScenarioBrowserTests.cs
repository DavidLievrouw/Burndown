using Burndown.Common;
using Burndown.Firefly;
using Microsoft.Playwright;
using Shouldly;

namespace Burndown.BrowserTests;

/// <summary>
/// Whole entry batches typed with the keyboard only, including the failure paths. Firefly is a scripted in-memory fake:
/// nothing here can reach a real Firefly III.
/// </summary>
[Collection(BrowserCollection.Name)]
[Trait("Category", "Browser")]
public sealed class EntryScenarioBrowserTests
{
    private const int CashAccountId = 16;

    private readonly BrowserFixture _fixture;

    public EntryScenarioBrowserTests(BrowserFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_mixed_batch_of_expense_income_and_transfer_is_submitted_with_the_right_accounts()
    {
        using var script = _fixture.Firefly.Script();
        var before = _fixture.Firefly.Created.Count;
        await using var context = await _fixture.NewContextAsync(1440, 900);
        var page = await _fixture.OpenEntryAsync(context);

        await EnterFirstExpenseAsync(page, "Bread", "12,50");

        // Row 2: income into Savings. Shift+Tab leaves When for the Type column.
        await page.Keyboard.PressAsync("Shift+Tab");
        await page.Keyboard.TypeAsync("i");
        await page.Keyboard.PressAsync("Tab"); // When
        await page.Keyboard.PressAsync("Tab"); // Description
        await page.Keyboard.TypeAsync("Salary");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("1000");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("sav");
        await page.Keyboard.PressAsync("Enter"); // the last field of an income row: on to a new row

        // Row 3: transfer from Savings to Checking.
        await page.Keyboard.PressAsync("Shift+Tab");
        await page.Keyboard.TypeAsync("t");
        await page.Keyboard.PressAsync("Tab"); // When
        await page.Keyboard.PressAsync("Tab"); // Description
        await page.Keyboard.TypeAsync("Move money");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("200");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("sav");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.TypeAsync("che");
        await page.Keyboard.PressAsync("Tab"); // after the last field: a blank row, which submit ignores
        await page.Keyboard.PressAsync("Control+Enter");

        await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("3 created in Firefly III");
        var created = _fixture.Firefly.Created.Skip(before).ToList();
        created.Count.ShouldBe(3);

        created[0].Type.ShouldBe(TransactionType.Withdrawal);
        created[0].SourceId.ShouldBe(AccountId.From(1));
        created[0].DestinationId.ShouldBe(AccountId.From(CashAccountId));
        created[0].Category.ShouldBe("Leven");
        created[0].Tags.ShouldBe(["SharedDavid"]);

        created[1].Type.ShouldBe(TransactionType.Deposit);
        created[1].Description.ShouldBe("Salary");
        created[1].Amount.ShouldBe(1000m);
        created[1].SourceId.ShouldBe(AccountId.From(CashAccountId));
        created[1].DestinationId.ShouldBe(AccountId.From(2));

        created[2].Type.ShouldBe(TransactionType.Transfer);
        created[2].Description.ShouldBe("Move money");
        created[2].Amount.ShouldBe(200m);
        created[2].SourceId.ShouldBe(AccountId.From(2));
        created[2].DestinationId.ShouldBe(AccountId.From(1));

        created[1].Date.ShouldBe(created[0].Date.AddMinutes(1));
        created[2].Date.ShouldBe(created[1].Date.AddMinutes(1));
        await Assertions.Expect(page.Locator("[data-status='created']")).ToHaveCountAsync(3);
    }

    [Fact]
    public async Task A_rejected_row_stays_with_its_field_error_and_the_other_rows_are_created()
    {
        using var script = _fixture.Firefly.Script();
        _fixture.Firefly.CreateFailure = transaction =>
            transaction.Description == "Reject"
                ? new Error(
                    FireflyErrorCodes.Validation,
                    "The given data was invalid.",
                    new Dictionary<string, string[]> { ["source_id"] = ["The selected source account is invalid."] }
                )
                : null;
        var before = _fixture.Firefly.Created.Count;
        await using var context = await _fixture.NewContextAsync(1440, 900);
        var page = await _fixture.OpenEntryAsync(context);

        await EnterFirstExpenseAsync(page, "Bread", "5");
        await FillRowAsync(page, "Reject", "6", replace: false);
        await page.Keyboard.PressAsync("Control+d");
        await FillRowAsync(page, "Butcher", "7", replace: true);
        await page.Keyboard.PressAsync("Control+Enter");

        var banner = page.GetByRole(AriaRole.Status);
        await Assertions.Expect(banner).ToContainTextAsync("2 created in Firefly III, 1 need attention");
        _fixture.Firefly.Created.Skip(before).Select(t => t.Description).ToList().ShouldBe(["Bread", "Butcher"]);

        var rejected = page.Locator("[data-entry-row-id][data-status='draft']").First;
        await Assertions.Expect(rejected).ToContainTextAsync("The selected source account is invalid.");
        await Assertions.Expect(rejected.GetByRole(AriaRole.Combobox, new() { Name = "Account" })).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("[data-status='created']")).ToHaveCountAsync(2);

        // Fixing the cause and submitting again sends only the rejected row.
        _fixture.Firefly.CreateFailure = null;
        await page.Keyboard.PressAsync("Control+Enter");
        await Assertions.Expect(banner).ToContainTextAsync("1 created in Firefly III");
        await Assertions.Expect(banner).Not.ToContainTextAsync("need attention");
        await Assertions.Expect(page.Locator("[data-status='created']")).ToHaveCountAsync(3);
        _fixture
            .Firefly.Created.Skip(before)
            .Select(t => t.Description)
            .ToList()
            .ShouldBe(["Bread", "Butcher", "Reject"]);
    }

    [Fact]
    public async Task An_uncertain_row_stops_the_batch_and_can_be_treated_as_not_sent()
    {
        using var script = _fixture.Firefly.Script();
        _fixture.Firefly.CreateFailure = transaction =>
            transaction.Description == "Slow"
                ? new Error(FireflyErrorCodes.Timeout, "Firefly III took too long to answer.")
                : null;
        var before = _fixture.Firefly.Created.Count;
        await using var context = await _fixture.NewContextAsync(1440, 900);
        var page = await _fixture.OpenEntryAsync(context);

        await EnterFirstExpenseAsync(page, "Bread", "5");
        await FillRowAsync(page, "Slow", "6", replace: false);
        await page.Keyboard.PressAsync("Control+d");
        await FillRowAsync(page, "Butcher", "7", replace: true);
        await page.Keyboard.PressAsync("Control+Enter");

        var banner = page.GetByRole(AriaRole.Status);
        await Assertions.Expect(banner).ToContainTextAsync("1 created in Firefly III");
        await Assertions.Expect(banner).ToContainTextAsync("1 unconfirmed");
        await Assertions.Expect(banner).ToContainTextAsync("1 not sent");
        await Assertions.Expect(page.Locator("[data-status='uncertain']")).ToHaveCountAsync(1);
        _fixture.Firefly.Created.Skip(before).Select(t => t.Description).ToList().ShouldBe(["Bread"]);

        // An unconfirmed row is never resent automatically: it is locked until the owner has checked Firefly.
        var treat = page.GetByRole(AriaRole.Button, new() { Name = "Treat 1 unconfirmed as not sent" });
        await Assertions.Expect(treat).ToBeVisibleAsync();
        await treat.ClickAsync();
        await Assertions.Expect(page.Locator("[data-status='uncertain']")).ToHaveCountAsync(0);

        _fixture.Firefly.CreateFailure = null;
        await page.GetByRole(AriaRole.Button, new() { Name = "Submit 2 rows" }).ClickAsync();
        await Assertions.Expect(banner).ToContainTextAsync("2 created in Firefly III");
        await Assertions.Expect(page.Locator("[data-status='created']")).ToHaveCountAsync(3);
        _fixture
            .Firefly.Created.Skip(before)
            .Select(t => t.Description)
            .ToList()
            .ShouldBe(["Bread", "Slow", "Butcher"]);
    }

    [Fact]
    public async Task A_rejected_access_token_stops_the_batch_and_says_so()
    {
        using var script = _fixture.Firefly.Script();
        _fixture.Firefly.CreateFailure = transaction =>
            transaction.Description == "Denied"
                ? new Error(FireflyErrorCodes.Unauthorized, "Firefly rejected the access token.")
                : null;
        var before = _fixture.Firefly.Created.Count;
        await using var context = await _fixture.NewContextAsync(1440, 900);
        var page = await _fixture.OpenEntryAsync(context);

        await EnterFirstExpenseAsync(page, "Bread", "5");
        await FillRowAsync(page, "Denied", "6", replace: false);
        await page.Keyboard.PressAsync("Control+d");
        await FillRowAsync(page, "Butcher", "7", replace: true);
        await page.Keyboard.PressAsync("Control+Enter");

        var banner = page.GetByRole(AriaRole.Status);
        await Assertions.Expect(banner).ToContainTextAsync("1 created in Firefly III");
        await Assertions.Expect(banner).ToContainTextAsync("1 need attention");
        await Assertions.Expect(banner).ToContainTextAsync("1 not sent");
        await Assertions.Expect(banner).ToContainTextAsync("Firefly III rejected the access token");
        _fixture.Firefly.Created.Skip(before).Count().ShouldBe(1);

        await page.SaveScreenshotAsync("entry-token-rejected");
        (await page.HorizontalOverflowAsync()).ShouldBeLessThanOrEqualTo(0);
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1024, 768)]
    public async Task A_long_row_status_and_banner_do_not_overflow_the_layout(int width, int height)
    {
        using var script = _fixture.Firefly.Script();
        _fixture.Firefly.CreateFailure = _ => new Error(
            FireflyErrorCodes.Validation,
            "The given data was invalid.",
            new Dictionary<string, string[]>
            {
                ["source_id"] = ["The selected source account is invalid, inactive or of the wrong account type."],
                ["amount"] = ["The amount must be greater than zero and may not exceed the allowed maximum."],
            }
        );
        await using var context = await _fixture.NewContextAsync(width, height);
        var page = await _fixture.OpenEntryAsync(context);

        await EnterFirstExpenseAsync(page, "Bread", "5");
        await page.Keyboard.PressAsync("Control+Enter");
        await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("1 need attention");

        (await page.HorizontalOverflowAsync()).ShouldBeLessThanOrEqualTo(0);
        var grid = page.GetByRole(AriaRole.Grid, new() { Name = "Entries to submit" });
        var box = await grid.BoundingBoxAsync();
        box!.Width.ShouldBeLessThanOrEqualTo(width);
        await page.SaveScreenshotAsync($"entry-rejected-{width}");
    }

    [Fact]
    public async Task The_grid_exposes_named_controls_a_listbox_and_a_rejected_field_to_assistive_technology()
    {
        using var script = _fixture.Firefly.Script();
        _fixture.Firefly.CreateFailure = _ => new Error(
            FireflyErrorCodes.Validation,
            "The given data was invalid.",
            new Dictionary<string, string[]> { ["source_id"] = ["The source account is invalid."] }
        );
        await using var context = await _fixture.NewContextAsync(1440, 900);
        var page = await _fixture.OpenEntryAsync(context);

        var unnamed = await page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('[role="grid"] input, [role="grid"] button, [role="grid"] [role="combobox"]')]
                .filter(e => !(e.getAttribute('aria-label') || e.getAttribute('aria-labelledby') || e.labels?.length || e.textContent?.trim()))
                .map(e => e.outerHTML.slice(0, 80))
            """
        );
        unnamed.ShouldBeEmpty();

        var account = page.GetByRole(AriaRole.Combobox, new() { Name = "Account" }).First;
        await account.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(page.GetByRole(AriaRole.Listbox)).ToBeVisibleAsync();
        await Assertions.Expect(account).ToHaveAttributeAsync("aria-expanded", "true");
        await page.Keyboard.PressAsync("Escape");

        await EnterFirstExpenseAsync(page, "Bread", "5");
        await page.Keyboard.PressAsync("Control+Enter");
        await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("1 need attention");
        await Assertions
            .Expect(page.GetByRole(AriaRole.Combobox, new() { Name = "Account" }).First)
            .ToHaveAttributeAsync("aria-invalid", "true");
    }

    /// <summary>Types a complete expense row from the Description field and ends on a new row's When field.</summary>
    private static async Task EnterFirstExpenseAsync(IPage page, string description, string amount)
    {
        await page.GetByRole(AriaRole.Combobox, new() { Name = "Description" }).FocusAsync();
        await page.Keyboard.TypeAsync(description);
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync(amount);
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("che");
        await page.Keyboard.PressAsync("Tab"); // account
        await page.Keyboard.TypeAsync("leven");
        await page.Keyboard.PressAsync("Tab"); // category
        await page.Keyboard.TypeAsync("sharedd");
        await page.Keyboard.PressAsync("Enter"); // tag
        await page.Keyboard.PressAsync("Enter"); // budget is optional: on to a new row
    }

    /// <summary>With focus on a row's When field, sets the description and amount and ends on the Amount field.</summary>
    private static async Task FillRowAsync(IPage page, string description, string amount, bool replace)
    {
        await page.Keyboard.PressAsync("Tab"); // When -> Description
        if (replace)
        {
            await page.Keyboard.PressAsync("Control+a");
        }

        await page.Keyboard.TypeAsync(description);
        await page.Keyboard.PressAsync("Enter");
        if (replace)
        {
            await page.Keyboard.PressAsync("Control+a");
        }

        await page.Keyboard.TypeAsync(amount);
    }
}
