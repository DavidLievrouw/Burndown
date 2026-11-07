using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Burndown.UnitTests.Web;

/// <summary>Hosts the app in memory with explicit, synthetic configuration and a temporary web root.</summary>
internal sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _environment;
    private readonly string _token;
    private readonly string _webRoot = Path.Combine(
        Path.GetTempPath(),
        "burndown-tests-" + Guid.NewGuid().ToString("N")
    );

    public TestWebApplicationFactory(string environment = "Development", string token = "test-token")
    {
        _environment = environment;
        _token = token;
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html><html><title>spa-shell</title></html>");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder
            .UseEnvironment(_environment)
            .UseWebRoot(_webRoot)
            .ConfigureAppConfiguration(
                (_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Firefly:BaseUrl"] = "http://firefly.invalid",
                            ["Firefly:Token"] = _token,
                        }
                    )
            );
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            DeleteWebRoot();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        DeleteWebRoot();
        GC.SuppressFinalize(this);
    }

    private void DeleteWebRoot()
    {
        if (Directory.Exists(_webRoot))
        {
            Directory.Delete(_webRoot, recursive: true);
        }
    }
}
