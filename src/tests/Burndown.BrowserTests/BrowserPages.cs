using Microsoft.Playwright;

namespace Burndown.BrowserTests;

/// <summary>Shared helpers to open pages in the browser tests.</summary>
internal static class BrowserPages
{
    public static async Task<IBrowserContext> NewContextAsync(this BrowserFixture fixture, int width, int height) =>
        await fixture.Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
            }
        );

    public static async Task<IPage> OpenOverviewAsync(this BrowserFixture fixture, IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl);
        return page;
    }

    public static async Task<IPage> OpenEntryAsync(this BrowserFixture fixture, IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + "/#/entry");
        await Assertions.Expect(page.GetByRole(AriaRole.Grid, new() { Name = "Entries to submit" })).ToBeVisibleAsync();
        return page;
    }

    /// <summary>The page has no horizontal scrollbar.</summary>
    public static async Task<int> HorizontalOverflowAsync(this IPage page) =>
        await page.EvaluateAsync<int>("document.documentElement.scrollWidth - document.documentElement.clientWidth");

    /// <summary>Saves a full-page screenshot when <c>BURNDOWN_SCREENSHOT_DIR</c> is set.</summary>
    public static async Task SaveScreenshotAsync(this IPage page, string name)
    {
        var screenshots = Environment.GetEnvironmentVariable("BURNDOWN_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(screenshots))
        {
            return;
        }

        Directory.CreateDirectory(screenshots);
        await page.ScreenshotAsync(
            new PageScreenshotOptions { Path = Path.Combine(screenshots, $"{name}.png"), FullPage = true }
        );
    }
}
