using System.Net;
using Burndown.Web;
using Shouldly;

namespace Burndown.UnitTests.Web;

public class HealthCheckCommandTests
{
    [Theory]
    [InlineData(null, 9142)]
    [InlineData("", 9142)]
    [InlineData("http://+:9142", 9142)]
    [InlineData("http://127.0.0.1:9042", 9042)]
    [InlineData("http://localhost:5000/", 5000)]
    [InlineData("http://+:7000;https://+:7001", 7000)]
    [InlineData("http://+", 9142)]
    [InlineData("http://+:99999", 9142)]
    [InlineData("http://+:0", 9142)]
    public void ResolvePort_reads_the_first_port(string? urls, int expected)
    {
        HealthCheckCommand.ResolvePort(urls).ShouldBe(expected);
    }

    [Fact]
    public async Task RunAsync_returns_zero_when_the_live_endpoint_answers_200()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(handler);
        await using var error = new StringWriter();

        var exitCode = await HealthCheckCommand.RunAsync(
            client,
            "http://+:9150",
            error,
            TestContext.Current.CancellationToken
        );

        exitCode.ShouldBe(0);
        handler.LastRequest!.RequestUri!.ToString().ShouldBe("http://127.0.0.1:9150/health/live");
        error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_returns_one_for_an_unhealthy_status()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = new HttpClient(handler);
        await using var error = new StringWriter();

        var exitCode = await HealthCheckCommand.RunAsync(client, null, error, TestContext.Current.CancellationToken);

        exitCode.ShouldBe(1);
        error.ToString().ShouldContain("503");
    }

    [Fact]
    public async Task RunAsync_returns_one_when_the_endpoint_is_unreachable()
    {
        using var handler = new StubHandler(_ => throw new HttpRequestException("refused"));
        using var client = new HttpClient(handler);
        await using var error = new StringWriter();

        var exitCode = await HealthCheckCommand.RunAsync(client, null, error, TestContext.Current.CancellationToken);

        exitCode.ShouldBe(1);
        error.ToString().ShouldContain("9142");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastRequest = request;
            return Task.FromResult(_respond(request));
        }
    }
}
