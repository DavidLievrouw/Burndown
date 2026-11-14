using Burndown.Firefly;
using Microsoft.Playwright;
using Shouldly;

namespace Burndown.BrowserTests;

/// <summary>
/// Smoke tests of the entry grid in a real browser against the real host. Firefly is an in-memory fake: nothing here
/// can reach a real Firefly III.
/// </summary>
[Collection(BrowserCollection.Name)]
[Trait("Category", "Browser")]
public sealed class EntryBrowserTests
{
    private readonly BrowserFixture _fixture;

    public EntryBrowserTests(BrowserFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_batch_is_entered_and_submitted_with_the_keyboard_only()
    {
        var before = _fixture.Firefly.Created.Count;
        await using var context = await NewContextAsync(1440, 900);
        var page = await OpenEntryAsync(context);

        await Assertions
            .Expect(page.GetByRole(AriaRole.Textbox, new() { Name = "When" }))
            .ToHaveValueAsync("15/02/2024 10:00");

        var description = page.GetByRole(AriaRole.Combobox, new() { Name = "Description" });
        await description.FocusAsync();
        await page.Keyboard.TypeAsync("Bakery");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("12,50");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("che");
        await page.Keyboard.PressAsync("Tab"); // account
        await page.Keyboard.TypeAsync("leven");
        await page.Keyboard.PressAsync("Tab"); // category
        await page.Keyboard.TypeAsync("sharedd");
        await page.Keyboard.PressAsync("Enter"); // tag
        await page.Keyboard.PressAsync("Enter"); // budget is optional, moves on to a new row

        var descriptions = page.GetByRole(AriaRole.Combobox, new() { Name = "Description" });
        var whens = page.GetByRole(AriaRole.Textbox, new() { Name = "When" });
        await Assertions.Expect(descriptions).ToHaveCountAsync(2);
        await Assertions.Expect(whens.Nth(1)).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("Enter"); // When keeps its sticky value
        await page.Keyboard.TypeAsync("Butcher");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("7.5");
        await page.Keyboard.PressAsync("Control+Enter");

        await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("2 created in Firefly III");
        var created = _fixture.Firefly.Created.Skip(before).ToList();
        created.Count.ShouldBe(2);
        created[0].Description.ShouldBe("Bakery");
        created[0].Amount.ShouldBe(12.5m);
        created[0].Type.ShouldBe(TransactionType.Withdrawal);
        created[0].Category.ShouldBe("Leven");
        created[0].Tags.ShouldBe(["SharedDavid"]);
        created[1].Description.ShouldBe("Butcher");
        created[1].Amount.ShouldBe(7.5m);
        created[1].Date.ShouldBe(created[0].Date.AddMinutes(1));
        created[1].SourceId.ShouldBe(created[0].SourceId);

        // Created rows are locked (plain disabled text boxes); one blank draft row remains and has focus.
        await Assertions.Expect(page.Locator("[data-status='created']")).ToHaveCountAsync(2);
        await Assertions.Expect(descriptions).ToHaveCountAsync(1);
        await Assertions.Expect(whens.Last).ToBeFocusedAsync();
        await Assertions.Expect(descriptions.First).ToHaveValueAsync(string.Empty);
    }

    [Fact]
    public async Task Ctrl_D_duplicates_the_row_and_Tab_after_the_last_field_adds_a_row()
    {
        await using var context = await NewContextAsync(1440, 900);
        var page = await OpenEntryAsync(context);
        var descriptions = page.GetByRole(AriaRole.Combobox, new() { Name = "Description" });
        var whens = page.GetByRole(AriaRole.Textbox, new() { Name = "When" });

        await descriptions.FocusAsync();
        await page.Keyboard.TypeAsync("Bakery");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("12,50");
        await page.Keyboard.PressAsync("Control+d");

        await Assertions.Expect(descriptions).ToHaveCountAsync(2);
        await Assertions.Expect(descriptions.Nth(1)).ToHaveValueAsync("Bakery");
        await Assertions.Expect(whens.Nth(1)).ToBeFocusedAsync();

        // From the copy's When, Tab walks description, amount, account, category, target and budget; one more leaves it.
        for (var i = 0; i < 7; i++)
        {
            await page.Keyboard.PressAsync("Tab");
        }

        await Assertions.Expect(descriptions).ToHaveCountAsync(3);
        await Assertions.Expect(whens.Nth(2)).ToBeFocusedAsync();
    }

    [Fact]
    public async Task An_incomplete_row_sends_nothing_and_focuses_the_problem()
    {
        var before = _fixture.Firefly.Created.Count;
        await using var context = await NewContextAsync(1440, 900);
        var page = await OpenEntryAsync(context);

        await page.GetByRole(AriaRole.Combobox, new() { Name = "Description" }).FocusAsync();
        await page.Keyboard.TypeAsync("Bakery");
        await page.Keyboard.PressAsync("Control+Enter");

        await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Nothing was sent");
        await Assertions.Expect(page.GetByRole(AriaRole.Textbox, new() { Name = "Amount" })).ToBeFocusedAsync();
        _fixture.Firefly.Created.Count.ShouldBe(before);
    }

    [Fact]
    public async Task Description_suggestions_are_accepted_with_Tab()
    {
        await using var context = await NewContextAsync(1440, 900);
        var page = await OpenEntryAsync(context);

        var description = page.GetByRole(AriaRole.Combobox, new() { Name = "Description" });
        await description.FocusAsync();
        await page.Keyboard.TypeAsync("Bakery D");
        await Assertions.Expect(page.GetByRole(AriaRole.Option, new() { Name = "Bakery Delhaize" })).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Tab");

        await Assertions.Expect(description).ToHaveValueAsync("Bakery Delhaize");
        await Assertions.Expect(page.GetByRole(AriaRole.Textbox, new() { Name = "Amount" })).ToBeFocusedAsync();
    }

    [Fact]
    public async Task The_date_is_edited_one_part_at_a_time()
    {
        await using var context = await NewContextAsync(1440, 900);
        var page = await OpenEntryAsync(context);
        var when = page.GetByRole(AriaRole.Textbox, new() { Name = "When" });

        await when.FocusAsync();
        (await Selection(when)).ShouldBe("15");

        await page.Keyboard.PressAsync("ArrowRight");
        (await Selection(when)).ShouldBe("02");
        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(when).ToHaveValueAsync("15/01/2024 10:00");

        await page.Keyboard.PressAsync("ArrowLeft");
        await page.Keyboard.TypeAsync("7");
        await Assertions.Expect(when).ToHaveValueAsync("07/01/2024 10:00");
        (await Selection(when)).ShouldBe("01"); // typing the day moved on to the month

        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.TypeAsync("2025");
        await Assertions.Expect(when).ToHaveValueAsync("07/01/2025 10:00");
        (await Selection(when)).ShouldBe("10"); // the hour

        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.TypeAsync("45");
        await Assertions.Expect(when).ToHaveValueAsync("07/01/2025 10:45");

        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.GetByRole(AriaRole.Combobox, new() { Name = "Description" })).ToBeFocusedAsync();

        static Task<string> Selection(ILocator locator) =>
            locator.EvaluateAsync<string>("e => e.value.substring(e.selectionStart, e.selectionEnd)");
    }

    [Fact]
    public async Task Enter_selects_the_highlighted_category_after_clearing_the_box()
    {
        await using var context = await NewContextAsync(1440, 900);
        var page = await OpenEntryAsync(context);
        var category = page.GetByRole(AriaRole.Combobox, new() { Name = "Category" });

        await page.GetByRole(AriaRole.Combobox, new() { Name = "Description" }).FocusAsync();
        await page.Keyboard.TypeAsync("Bakery");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("12,50");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("che");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.TypeAsync("leven");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(category).ToBeFocusedAsync();
        await Assertions.Expect(category).ToHaveValueAsync("Leven");

        await page.Keyboard.PressAsync("Backspace");
        await Assertions.Expect(page.GetByRole(AriaRole.Listbox)).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(category).Not.ToHaveValueAsync("Leven");
        await Assertions.Expect(category).Not.ToHaveValueAsync(string.Empty);
        await Assertions.Expect(page.GetByRole(AriaRole.Combobox, new() { Name = "Target" })).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1024, 768)]
    public async Task The_grid_fits_without_horizontal_overflow_and_errors_do_not_shift_the_layout(
        int width,
        int height
    )
    {
        await using var context = await NewContextAsync(width, height);
        var page = await OpenEntryAsync(context);
        var grid = page.GetByRole(AriaRole.Grid, new() { Name = "Entries to submit" });
        await Assertions.Expect(grid).ToBeVisibleAsync();

        var row = page.Locator("[data-entry-row-id]").First;
        var beforeBox = await row.BoundingBoxAsync();
        var gridBefore = await grid.BoundingBoxAsync();

        await page.GetByRole(AriaRole.Combobox, new() { Name = "Description" }).FocusAsync();
        await page.Keyboard.TypeAsync("Bakery");
        await page.Keyboard.PressAsync("Control+Enter");
        await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Nothing was sent");

        var afterBox = await row.BoundingBoxAsync();
        afterBox!.Height.ShouldBe(beforeBox!.Height);
        afterBox.Y.ShouldBe(beforeBox.Y);
        var gridAfter = await grid.BoundingBoxAsync();
        gridAfter!.Width.ShouldBe(gridBefore!.Width);

        var overflow = await page.EvaluateAsync<int>(
            "document.documentElement.scrollWidth - document.documentElement.clientWidth"
        );
        overflow.ShouldBeLessThanOrEqualTo(0);

        var screenshots = Environment.GetEnvironmentVariable("BURNDOWN_SCREENSHOT_DIR");
        if (!string.IsNullOrEmpty(screenshots))
        {
            Directory.CreateDirectory(screenshots);
            await page.ScreenshotAsync(
                new PageScreenshotOptions { Path = Path.Combine(screenshots, $"entry-{width}.png"), FullPage = true }
            );
        }
    }

    private async Task<IBrowserContext> NewContextAsync(int width, int height) =>
        await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
            }
        );

    private async Task<IPage> OpenEntryAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl + "/#/entry");
        await Assertions.Expect(page.GetByRole(AriaRole.Grid, new() { Name = "Entries to submit" })).ToBeVisibleAsync();
        return page;
    }
}
