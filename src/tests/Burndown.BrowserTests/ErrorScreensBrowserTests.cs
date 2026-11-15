using Burndown.Common;
using Burndown.Firefly;
using Microsoft.Playwright;
using Shouldly;

namespace Burndown.BrowserTests;

/// <summary>What the owner sees when Firefly III is down or refuses the token, and that the screens recover.</summary>
[Collection(BrowserCollection.Name)]
[Trait("Category", "Browser")]
public sealed class ErrorScreensBrowserTests
{
    private readonly BrowserFixture _fixture;

    public ErrorScreensBrowserTests(BrowserFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(FireflyErrorCodes.Unauthorized, "Firefly III rejected the access token", 1440, 900)]
    [InlineData(FireflyErrorCodes.Unavailable, "Firefly III is not reachable", 1024, 768)]
    [InlineData(FireflyErrorCodes.Timeout, "Firefly III took too long to answer", 1440, 900)]
    public async Task The_overview_explains_the_failure_and_recovers_after_Try_again(
        string code,
        string title,
        int width,
        int height
    )
    {
        using var script = _fixture.Firefly.Script();
        _fixture.Firefly.ReadFailure = new Error(code, "Scripted failure.");
        await using var context = await _fixture.NewContextAsync(width, height);
        var page = await _fixture.OpenOverviewAsync(context);

        var alert = page.GetByRole(AriaRole.Alert);
        await Assertions.Expect(alert).ToBeVisibleAsync();
        await Assertions.Expect(alert.GetByRole(AriaRole.Heading, new() { Name = title })).ToBeVisibleAsync();
        (await page.HorizontalOverflowAsync()).ShouldBeLessThanOrEqualTo(0);
        await page.SaveScreenshotAsync($"overview-error-{code}-{width}");

        _fixture.Firefly.ReadFailure = null;
        await alert.GetByRole(AriaRole.Button, new() { Name = "Try again" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "February 2024" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task The_entry_page_explains_an_unreachable_server_and_recovers()
    {
        await using var context = await _fixture.NewContextAsync(1440, 900);
        var page = await context.NewPageAsync();
        await page.RouteAsync("**/api/entry/reference", route => route.AbortAsync());
        await page.GotoAsync(_fixture.BaseUrl + "/#/entry");

        var alert = page.GetByRole(AriaRole.Alert);
        await Assertions
            .Expect(alert.GetByRole(AriaRole.Heading, new() { Name = "The Burndown server is not reachable" }))
            .ToBeVisibleAsync();

        await page.UnrouteAsync("**/api/entry/reference");
        await alert.GetByRole(AriaRole.Button, new() { Name = "Try again" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Grid, new() { Name = "Entries to submit" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Reloading_the_accounts_reports_a_rejected_token_and_a_later_reload_recovers()
    {
        using var script = _fixture.Firefly.Script();
        await using var context = await _fixture.NewContextAsync(1440, 900);
        var page = await _fixture.OpenEntryAsync(context);

        _fixture.Firefly.ReadFailure = new Error(FireflyErrorCodes.Unauthorized, "Scripted failure.");
        var reload = page.GetByRole(AriaRole.Button, new() { Name = "Reload accounts" });
        await reload.ClickAsync();

        var banner = page.GetByRole(AriaRole.Status);
        await Assertions.Expect(banner).ToContainTextAsync("Firefly III rejected the access token");
        await page.SaveScreenshotAsync("entry-reload-token-rejected");
        (await page.HorizontalOverflowAsync()).ShouldBeLessThanOrEqualTo(0);

        // The grid stays usable on the accounts that were loaded before.
        await Assertions.Expect(page.GetByRole(AriaRole.Grid, new() { Name = "Entries to submit" })).ToBeVisibleAsync();

        _fixture.Firefly.ReadFailure = null;
        await reload.ClickAsync();
        await Assertions.Expect(banner).ToContainTextAsync("were reloaded");
    }

    [Fact]
    public async Task An_overview_month_needs_a_bounded_number_of_Firefly_requests()
    {
        using var script = _fixture.Firefly.Script();
        await using var context = await _fixture.NewContextAsync(1440, 900);
        var before = _fixture.Firefly.TransactionRequests;
        var page = await _fixture.OpenOverviewAsync(context);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "February 2024" })).ToBeVisibleAsync();

        var used = _fixture.Firefly.TransactionRequests - before;
        used.ShouldBeGreaterThan(0);
        used.ShouldBeLessThanOrEqualTo(6);
    }
}
