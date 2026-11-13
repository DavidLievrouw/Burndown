using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Burndown.Firefly;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Burndown.CharacteristicsTests.Infrastructure;

/// <summary>
/// A throwaway Firefly III instance (SQLite, synthetic data) shared by all characteristics tests.
/// It never touches the owner's real Firefly III.
/// </summary>
public sealed partial class FireflyFixture : IAsyncLifetime
{
    /// <summary>The pinned image; update deliberately and re-run the characteristics tests.</summary>
    public const string Image = "fireflyiii/core:version-6.6.6";

    private const int ContainerPort = 8080;

    private IContainer? _container;
    private HttpClient? _seedHttp;

    public string BaseUrl { get; private set; } = string.Empty;

    public string Token { get; private set; } = string.Empty;

    public AccountId CheckingId { get; private set; }

    public AccountId SavingsId { get; private set; }

    public AccountId CashId { get; private set; }

    public BudgetId DavidBudgetId { get; private set; }

    public BudgetId TinaBudgetId { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var script = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Infrastructure", "bootstrap.php")
        );
        _container = new ContainerBuilder(Image)
            .WithPortBinding(ContainerPort, true)
            .WithEnvironment("DB_CONNECTION", "sqlite")
            .WithEnvironment("DB_DATABASE", "/var/www/html/storage/database/database.sqlite")
            .WithEnvironment("APP_KEY", RandomAppKey())
            .WithEnvironment("SITE_OWNER", "owner@example.test")
            .WithEnvironment("TZ", "Europe/Brussels")
            .WithEnvironment("APP_ENV", "local")
            .WithEnvironment("APP_DEBUG", "false")
            .WithEnvironment("TRUSTED_PROXIES", "**")
            .WithEnvironment("DEFAULT_LANGUAGE", "en_US")
            .WithEnvironment("DEFAULT_LOCALE", "equal")
            .WithResourceMapping(script, "/tmp/burndown-bootstrap.php")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("NGINX \\+ PHP-FPM is running correctly"))
            .Build();
        await _container.StartAsync();

        BaseUrl = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(ContainerPort)}";
        Token = await CreateTokenAsync(_container);

        _seedHttp = new HttpClient { BaseAddress = new Uri(BaseUrl + "/api/v1/") };
        _seedHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        _seedHttp.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        await SeedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _seedHttp?.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Creates a client for the throwaway Firefly. Page size is small by default to exercise pagination.</summary>
    /// <param name="pageSize">The page size the client requests.</param>
    /// <param name="token">An alternative token, for example an invalid one. Defaults to the seeded token.</param>
    /// <returns>A client for the container.</returns>
    internal FireflyClient CreateClient(int pageSize = 2, string? token = null)
    {
        var http = new HttpClient { BaseAddress = new Uri(BaseUrl + "/api/v1/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token ?? Token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return new FireflyClient(http, NullLogger<FireflyClient>.Instance, pageSize);
    }

    private static string RandomAppKey() =>
        string.Create(
            32,
            0,
            static (span, _) => RandomNumberGenerator.GetItems("abcdefghijklmnopqrstuvwxyz0123456789".AsSpan(), span)
        );

    private static async Task<string> CreateTokenAsync(IContainer container)
    {
        var result = await container.ExecAsync(["php", "/tmp/burndown-bootstrap.php"]);
        var match = TokenPattern().Match(result.Stdout);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Could not create a Firefly token (exit code {result.ExitCode}): {result.Stderr}"
            );
        }

        return match.Groups[1].Value;
    }

    private async Task SeedAsync()
    {
        CheckingId = AccountId.From(await CreateAccountAsync("Checking", "asset", "defaultAsset"));
        SavingsId = AccountId.From(await CreateAccountAsync("Savings", "asset", "savingAsset"));
        await CreateAccountAsync("Employer", "revenue", null);
        DavidBudgetId = BudgetId.From(await CreateBudgetAsync("David"));
        TinaBudgetId = BudgetId.From(await CreateBudgetAsync("Tina"));

        // Firefly creates its built-in cash account on demand, so its id is not predictable.
        await PostAsync(
            "transactions",
            new
            {
                transactions = new[]
                {
                    new
                    {
                        type = "withdrawal",
                        date = "2026-01-01T09:00:00+01:00",
                        amount = "1",
                        description = "Seed cash account",
                        source_id = CheckingId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    },
                },
            }
        );
        using var cash = await GetJsonAsync("accounts?type=cash");
        CashId = AccountId.From(int.Parse(cash.RootElement.GetProperty("data")[0].GetProperty("id").GetString()!));
    }

    private async Task<int> CreateAccountAsync(string name, string type, string? role)
    {
        using var response = await PostAsync(
            "accounts",
            new
            {
                name,
                type,
                account_role = role,
            }
        );
        return ReadId(response);
    }

    private async Task<int> CreateBudgetAsync(string name)
    {
        using var response = await PostAsync("budgets", new { name });
        return ReadId(response);
    }

    private static int ReadId(JsonDocument response) =>
        int.Parse(response.RootElement.GetProperty("data").GetProperty("id").GetString()!);

    private async Task<JsonDocument> PostAsync(string path, object body)
    {
        using var response = await _seedHttp!.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Seeding {path} failed with {(int)response.StatusCode}: {text}");
        }

        return JsonDocument.Parse(text);
    }

    private async Task<JsonDocument> GetJsonAsync(string path)
    {
        var text = await _seedHttp!.GetStringAsync(path);
        return JsonDocument.Parse(text);
    }

    [GeneratedRegex(@"TOKEN:(eyJ[\w\-\.]+)")]
    private static partial Regex TokenPattern();
}

/// <summary>Shares one throwaway Firefly across every test class in the collection.</summary>
[CollectionDefinition(Name)]
public sealed class FireflyCollection : ICollectionFixture<FireflyFixture>
{
    public const string Name = "Throwaway Firefly";
}
