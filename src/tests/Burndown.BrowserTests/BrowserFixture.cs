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
    private IPlaywright? _playwright;

    public string BaseUrl { get; private set; } = string.Empty;

    public IBrowser Browser { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
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
                    services.AddSingleton<IFireflyClient>(new FakeFireflyClient(SyntheticData.Transactions));
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
    }
}

/// <summary>Shares one host and browser across the browser tests.</summary>
[CollectionDefinition(Name)]
public sealed class BrowserCollection : ICollectionFixture<BrowserFixture>
{
    public const string Name = "Browser";
}
