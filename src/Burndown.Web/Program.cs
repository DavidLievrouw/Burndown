using Burndown.Common;
using Burndown.Entry;
using Burndown.Firefly;
using Burndown.Overview;
using Burndown.Web;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

if (args is [HealthCheckCommand.Switch])
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    return await HealthCheckCommand.RunAsync(
        client,
        Environment.GetEnvironmentVariable("ASPNETCORE_URLS"),
        Console.Error
    );
}

const string loopbackUrl = "http://127.0.0.1:9142";
string[] devClientOrigins = ["http://localhost:5173", "http://127.0.0.1:5173"];

var builder = WebApplication.CreateBuilder(args);

// Direct runs bind to loopback only. A configured URL (the container sets ASPNETCORE_URLS=http://+:9142) wins.
if (string.IsNullOrEmpty(builder.Configuration[WebHostDefaults.ServerUrlsKey]))
{
    builder.WebHost.UseUrls(loopbackUrl);
}

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddSerilog(
    (_, loggerConfiguration) =>
        loggerConfiguration
            .MinimumLevel.Is(builder.Environment.IsDevelopment() ? LogEventLevel.Debug : LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
);

builder.Services.AddSingleton<IValidateOptions<BurndownOptions>, BurndownOptionsValidator>();
builder.Services.AddOptions<BurndownOptions>().BindConfiguration(BurndownOptions.Section).ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(provider =>
{
    var options = provider.GetRequiredService<IOptions<BurndownOptions>>().Value;
    AppClock.TryResolveZone(options.TimeZone, out var zone);
    return new AppClock(provider.GetRequiredService<TimeProvider>(), zone!);
});

builder.Services.AddFireflyServices().AddOverviewServices().AddEntryServices();

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

app.UseSerilogRequestLogging(options =>
    options.GetLevel = (httpContext, _, exception) =>
        exception is null && httpContext.Request.Path.StartsWithSegments("/health")
            ? LogEventLevel.Verbose
            : LogEventLevel.Information
);
app.UseMiddleware<RequestOriginProtectionMiddleware>((object)(app.Environment.IsDevelopment() ? devClientOrigins : []));
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions { Predicate = registration => registration.Tags.Contains(HealthTags.Ready) }
);

var api = app.MapGroup("/api");
api.MapOverviewEndpoints();
api.MapFallback(() => Results.NotFound());
app.MapFallbackToFile("index.html");

await app.RunAsync();
return 0;

/// <summary>Entry point marker, public so that <c>WebApplicationFactory</c> can host the app in tests.</summary>
public partial class Program;
