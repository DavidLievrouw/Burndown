using Burndown.Firefly;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Playwright;

namespace Burndown.BrowserTests;

/// <summary>
/// Runs the real host on a real Kestrel listener (never the in-memory TestServer) with synthetic data, plus one
/// Chromium instance for the whole suite. The client must be built first (<c>npm run build</c> in
/// <c>src/Burndown.Client</c>) and the browsers installed once (<c>playwright.ps1 install chromium</c> after a build).
/// </summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private const string UrlsVariable = "ASPNETCORE_URLS";

    private IPlaywright? _playwright;
    private string? _previousUrls;

    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>The in-memory Firefly the host talks to; shared by all browser tests.</summary>
    internal FakeFireflyClient Firefly { get; } = new(SyntheticData.Transactions);

    public IBrowser Browser { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        // Without a configured URL the host binds its fixed default port (9042), which the owner's container may hold.
        _previousUrls = Environment.GetEnvironmentVariable(UrlsVariable);
        Environment.SetEnvironmentVariable(UrlsVariable, "http://127.0.0.1:0");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder
                .UseEnvironment("Development")
                .ConfigureAppConfiguration(
                    (_, configuration) =>
                        configuration.AddInMemoryCollection(
                            new Dictionary<string, string?>
                            {
                                ["Firefly:BaseUrl"] = "http://firefly.invalid",
                                ["Firefly:Token"] = "test-token",
                            }
                        )
                )
                .ConfigureServices(services =>
                {
                    services.RemoveAll<IFireflyClient>();
                    services.AddSingleton<IFireflyClient>(Firefly);
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(new FakeTimeProvider(SyntheticData.Now));
                })
        );
        _factory.UseKestrel(0);
        _factory.StartServer();
        BaseUrl = _factory
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First()
            .TrimEnd('/');

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        Environment.SetEnvironmentVariable(UrlsVariable, _previousUrls);
    }
}

/// <summary>Shares one host and browser across the browser tests.</summary>
[CollectionDefinition(Name)]
public sealed class BrowserCollection : ICollectionFixture<BrowserFixture>
{
    public const string Name = "Browser";
}
