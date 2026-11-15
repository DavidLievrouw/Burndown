using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Burndown.Common;
using Burndown.Firefly;
using Burndown.UnitTests.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using static Burndown.UnitTests.Overview.OverviewTestData;

namespace Burndown.UnitTests.Overview;

public class OverviewEndpointTests
{
    private static HttpClient CreateClient(TestWebApplicationFactory factory, FakeFireflyClient firefly) =>
        factory
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IFireflyClient>();
                    services.AddSingleton<IFireflyClient>(firefly);
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(
                        new FakeTimeProvider(DateTimeOffset.Parse("2024-02-15T10:00:00Z", CultureInfo.InvariantCulture))
                    );
                })
            )
            .CreateClient();

    [Fact]
    public async Task Serves_the_overview_as_camel_case_json()
    {
        await using var factory = new TestWebApplicationFactory();
        var firefly = FakeFireflyClient.Returning(Deposit(2024, 1, 5, 2900m), Withdrawal(2024, 2, 2, 50.5m, "Shop"));
        using var client = CreateClient(factory, firefly);

        var response = await client.GetAsync("/api/overview?month=2024-02", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("month").GetString().ShouldBe("2024-02");
        json.GetProperty("today").GetString().ShouldBe("2024-02-15");
        json.GetProperty("budget").GetProperty("effective").GetDecimal().ShouldBe(2900m);
        json.GetProperty("totals").GetProperty("spent").GetDecimal().ShouldBe(50.5m);
        var days = json.GetProperty("days");
        days.GetArrayLength().ShouldBe(29);
        days[1].GetProperty("description").GetString().ShouldBe("Shop");
        days[1].GetProperty("perDayTone").GetString().ShouldBe("green");
        days[20].GetProperty("isFuture").GetBoolean().ShouldBeTrue();
        json.GetProperty("chart").GetProperty("todayDay").GetInt32().ShouldBe(15);
        json.GetProperty("chart")
            .GetProperty("points")[20]
            .GetProperty("actual")
            .ValueKind.ShouldBe(JsonValueKind.Null);
        firefly.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Serves_the_year_overview_as_camel_case_json()
    {
        await using var factory = new TestWebApplicationFactory();
        var firefly = FakeFireflyClient.Returning(Deposit(2023, 12, 5, 3000m), Withdrawal(2024, 1, 2, 500m, "Shop"));
        using var client = CreateClient(factory, firefly);

        var response = await client.GetAsync("/api/overview/year?month=2024-02", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("year").GetInt32().ShouldBe(2024);
        json.GetProperty("total").GetDecimal().ShouldBe(2500m);
        json.GetProperty("months")[0].GetProperty("month").GetString().ShouldBe("2024-01");
        json.GetProperty("months")[0].GetProperty("result").GetDecimal().ShouldBe(2500m);
    }

    [Fact]
    public async Task Answers_400_for_an_invalid_year_overview_month()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = CreateClient(factory, FakeFireflyClient.Returning());

        var response = await client.GetAsync("/api/overview/year?month=2024-04", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("errors").TryGetProperty("month", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Applies_the_budget_override_from_the_query_string()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = CreateClient(factory, FakeFireflyClient.Returning(Deposit(2024, 1, 5, 2900m)));

        var response = await client.GetAsync(
            "/api/overview?month=2024-02&budgetOverride=100",
            TestContext.Current.CancellationToken
        );

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("budget").GetProperty("effective").GetDecimal().ShouldBe(100m);
        json.GetProperty("budget").GetProperty("overridden").GetBoolean().ShouldBeTrue();
    }

    [Theory]
    [InlineData("?month=2023-11", "month")]
    [InlineData("?month=2024-04", "month")]
    [InlineData("?month=garbage", "month")]
    [InlineData("?budgetOverride=", "budgetOverride")]
    [InlineData("?budgetOverride=-5", "budgetOverride")]
    public async Task Answers_400_with_field_errors_for_an_invalid_request(string query, string field)
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = CreateClient(factory, FakeFireflyClient.Returning());

        var response = await client.GetAsync("/api/overview" + query, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("code").GetString().ShouldBe(ErrorCodes.Validation);
        json.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData(FireflyErrorCodes.Unauthorized, 502)]
    [InlineData(FireflyErrorCodes.Forbidden, 502)]
    [InlineData(FireflyErrorCodes.Unexpected, 502)]
    [InlineData(FireflyErrorCodes.InvalidResponse, 502)]
    [InlineData(FireflyErrorCodes.Unavailable, 503)]
    [InlineData(FireflyErrorCodes.Timeout, 504)]
    public async Task Maps_Firefly_failures_to_distinct_statuses_without_leaking_details(string code, int status)
    {
        await using var factory = new TestWebApplicationFactory();
        var firefly = new FakeFireflyClient(
            (_, _, _) => Result<IReadOnlyList<FireflyTransaction>>.Failure(new Error(code, "A user-readable message."))
        );
        using var client = CreateClient(factory, firefly);

        var response = await client.GetAsync("/api/overview", TestContext.Current.CancellationToken);

        ((int)response.StatusCode).ShouldBe(status);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("code").GetString().ShouldBe(code);
        json.GetProperty("detail").GetString().ShouldBe("A user-readable message.");
    }

    [Fact]
    public async Task Does_not_accept_state_changing_methods()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = CreateClient(factory, FakeFireflyClient.Returning());

        var response = await client.PostAsync(
            "/api/overview",
            JsonContent.Create(new { }),
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }
}
