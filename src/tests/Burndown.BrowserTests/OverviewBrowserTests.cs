using Microsoft.Playwright;
using Shouldly;

namespace Burndown.BrowserTests;

/// <summary>Smoke tests of the overview in a real browser against the real host and a synthetic Firefly.</summary>
[Collection(BrowserCollection.Name)]
[Trait("Category", "Browser")]
public sealed class OverviewBrowserTests
{
    private readonly BrowserFixture _fixture;

    public OverviewBrowserTests(BrowserFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1024, 768)]
    public async Task Overview_renders_without_horizontal_overflow(int width, int height)
    {
        await using var context = await NewContextAsync(width, height);
        var page = await OpenOverviewAsync(context);

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "February 2024" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByLabel("Daily overview")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByLabel("Burndown chart")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByLabel("Year overview")).ToContainTextAsync("Year 2024, result so far");
        await Assertions.Expect(page.Locator("[data-testid='year-month']")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("tr[aria-current='date']")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".recharts-line").First).ToBeVisibleAsync();

        var overflow = await page.EvaluateAsync<int>(
            "document.documentElement.scrollWidth - document.documentElement.clientWidth"
        );
        overflow.ShouldBeLessThanOrEqualTo(0);

        var screenshots = Environment.GetEnvironmentVariable("BURNDOWN_SCREENSHOT_DIR");
        if (!string.IsNullOrEmpty(screenshots))
        {
            Directory.CreateDirectory(screenshots);
            await page.ScreenshotAsync(
                new PageScreenshotOptions { Path = Path.Combine(screenshots, $"overview-{width}.png"), FullPage = true }
            );
        }
    }

    [Fact]
    public async Task Month_navigation_works_with_the_keyboard_only()
    {
        await using var context = await NewContextAsync(1440, 900);
        var page = await OpenOverviewAsync(context);
        var heading = page.GetByRole(AriaRole.Heading, new() { Name = "February 2024" });
        await Assertions.Expect(heading).ToBeVisibleAsync();

        await page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "January 2024" })).ToBeVisibleAsync();

        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(heading).ToBeVisibleAsync();

        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "March 2024" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Next month" })).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("[data-testid='year-month']")).ToHaveCountAsync(2);
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
    }

    private async Task<IBrowserContext> NewContextAsync(int width, int height) =>
        await _fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
            }
        );

    private async Task<IPage> OpenOverviewAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);
        return page;
    }
}
