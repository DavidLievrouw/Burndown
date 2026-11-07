using System.Net;
using System.Net.Http.Headers;
using Shouldly;

namespace Burndown.UnitTests.Web;

public class HostEndpointTests
{
    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        string? origin,
        string? contentType = "application/json"
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = new StringContent("{}");
        request.Content.Headers.ContentType = contentType is null ? null : MediaTypeHeaderValue.Parse(contentType);
        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Live_health_check_does_not_depend_on_firefly()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_health_check_is_unhealthy_until_the_firefly_check_exists()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/overview/2024-05")]
    public async Task Spa_routes_serve_the_shell(string path)
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("<html");
    }

    [Fact]
    public async Task Unknown_api_routes_return_404_instead_of_the_spa_shell()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("<html");
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://127.0.0.1")]
    [InlineData(null)]
    public async Task State_changing_requests_from_the_own_origin_or_without_origin_pass(string? origin)
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await PostAsync(client, "/api/does-not-exist", origin);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("http://evil.example")]
    [InlineData("https://localhost")]
    [InlineData("http://localhost:9999")]
    [InlineData("null")]
    public async Task State_changing_requests_from_a_foreign_origin_are_rejected(string origin)
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await PostAsync(client, "/api/does-not-exist", origin);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData(null)]
    public async Task State_changing_requests_must_be_json(string? contentType)
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await PostAsync(client, "/api/does-not-exist", origin: null, contentType);

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task The_vite_dev_origin_is_accepted_in_development_only()
    {
        await using var development = new TestWebApplicationFactory("Development");
        await using var production = new TestWebApplicationFactory("Production");
        using var developmentClient = development.CreateClient();
        using var productionClient = production.CreateClient();

        var inDevelopment = await PostAsync(developmentClient, "/api/x", "http://localhost:5173");
        var inProduction = await PostAsync(productionClient, "/api/x", "http://localhost:5173");

        inDevelopment.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        inProduction.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Hosts_other_than_loopback_are_rejected()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Host = "evil.example";

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_app_does_not_start_without_a_firefly_token()
    {
        await using var factory = new TestWebApplicationFactory(token: "");

        var exception = Should.Throw<Exception>(() => factory.CreateClient());

        exception.ToString().ShouldContain("Firefly:Token");
        await Task.CompletedTask;
    }
}
