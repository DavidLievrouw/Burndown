using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Burndown.Firefly;
using Burndown.UnitTests.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Burndown.UnitTests.Entry;

public class EntryEndpointTests
{
    private static HttpClient CreateClient(TestWebApplicationFactory factory, RecordingFireflyClient firefly) =>
        factory
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IFireflyClient>();
                    services.AddSingleton<IFireflyClient>(firefly);
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(
                        new FakeTimeProvider(DateTimeOffset.Parse("2026-05-15T10:00:00Z", CultureInfo.InvariantCulture))
                    );
                })
            )
            .CreateClient();

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private const string Row =
        """{"type":"expense","dateTime":"2026-05-15T09:30","description":"Bakery","amount":12.5,"accountId":1,"category":"Leven","tag":"SharedDavid","budgetId":13}""";

    [Fact]
    public async Task Serves_the_reference_data_as_camel_case_json()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = CreateClient(factory, new RecordingFireflyClient());

        var response = await client.GetAsync("/api/entry/reference", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("today").GetString().ShouldBe("2026-05-15");
        json.GetProperty("timeZone").GetString().ShouldBe("Europe/Brussels");
        json.GetProperty("accounts")[0].GetProperty("name").GetString().ShouldBe("Checking");
        json.GetProperty("maxBatchSize").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Refresh_reloads_the_accounts()
    {
        await using var factory = new TestWebApplicationFactory();
        var firefly = new RecordingFireflyClient();
        using var client = CreateClient(factory, firefly);

        await client.GetAsync("/api/entry/reference", TestContext.Current.CancellationToken);
        var response = await client.PostAsync(
            "/api/entry/reference/refresh",
            Json("{}"),
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        firefly.AccountCalls.ShouldBe(2);
    }

    [Fact]
    public async Task Serves_the_description_suggestions()
    {
        await using var factory = new TestWebApplicationFactory();
        var firefly = new RecordingFireflyClient();
        using var client = CreateClient(factory, firefly);

        var response = await client.GetAsync(
            "/api/entry/suggest/descriptions?q=bak",
            TestContext.Current.CancellationToken
        );

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("descriptions")[0].GetString().ShouldBe("bak one");
    }

    [Fact]
    public async Task Maps_a_firefly_failure_on_the_reference_data_to_a_problem()
    {
        await using var factory = new TestWebApplicationFactory();
        var firefly = new RecordingFireflyClient
        {
            AccountsScript = () =>
                Burndown.Common.Result<IReadOnlyList<FireflyAccount>>.Failure(
                    new Burndown.Common.Error(FireflyErrorCodes.Unauthorized, "token")
                ),
        };
        using var client = CreateClient(factory, firefly);

        var response = await client.GetAsync("/api/entry/reference", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("code").GetString().ShouldBe(FireflyErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task Submits_a_batch_and_reports_each_row()
    {
        await using var factory = new TestWebApplicationFactory();
        var firefly = new RecordingFireflyClient();
        using var client = CreateClient(factory, firefly);
        var invalid = Row.Replace("\"amount\":12.5", "\"amount\":0");

        var response = await client.PostAsync(
            "/api/entry/submit",
            Json($$"""{"rows":[{{Row}},{{invalid}}]}"""),
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var results = json.GetProperty("results");
        results[0].GetProperty("status").GetString().ShouldBe("created");
        results[0].GetProperty("id").GetInt32().ShouldBe(100);
        results[1].GetProperty("status").GetString().ShouldBe("invalid");
        results[1].GetProperty("errors").GetProperty("amount").GetArrayLength().ShouldBe(1);
        json.GetProperty("summary").GetProperty("created").GetInt32().ShouldBe(1);
        firefly.Created.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Rejects_an_empty_batch_with_a_400_that_names_the_field()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = CreateClient(factory, new RecordingFireflyClient());

        var response = await client.PostAsync(
            "/api/entry/submit",
            Json("""{"rows":[]}"""),
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("errors").GetProperty("rows").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Refuses_a_submit_that_is_not_json_and_sends_nothing()
    {
        await using var factory = new TestWebApplicationFactory();
        var firefly = new RecordingFireflyClient();
        using var client = CreateClient(factory, firefly);

        var response = await client.PostAsync(
            "/api/entry/submit",
            new StringContent($$"""{"rows":[{{Row}}]}""", Encoding.UTF8, "text/plain"),
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        firefly.Created.ShouldBeEmpty();
    }

    [Fact]
    public async Task Refuses_a_submit_from_a_foreign_origin_and_sends_nothing()
    {
        await using var factory = new TestWebApplicationFactory();
        var firefly = new RecordingFireflyClient();
        using var client = CreateClient(factory, firefly);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/entry/submit")
        {
            Content = Json($$"""{"rows":[{{Row}}]}"""),
        };
        request.Headers.TryAddWithoutValidation("Origin", "https://evil.example");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        firefly.Created.ShouldBeEmpty();
    }
}
