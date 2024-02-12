using Burndown.Components;
using Burndown.Config;
using Burndown.Services;
using Microsoft.Extensions.Options;
using Radzen;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .Configure<FireflySettings>(builder.Configuration.GetSection("Firefly"))
    .AddHttpContextAccessor()
    .AddSingleton<AuthorizationService>()
    .AddHttpClient<AuthorizationService>(
        (provider, client) => {
            var fireflySettings = provider.GetRequiredService<IOptions<FireflySettings>>();
            client.BaseAddress = fireflySettings.Value.BackChannelBaseAddress;
        }
    ).Services
    .AddSingleton<FireflyQueryService>()
    .AddHttpClient<FireflyQueryService>(
        (provider, client) => {
            var fireflySettings = provider.GetRequiredService<IOptions<FireflySettings>>();
            client.BaseAddress = fireflySettings.Value.BackChannelBaseAddress;
        }
    ).Services
    .AddSingleton<DataProcessor>()
    .AddRazorComponents()
    .AddInteractiveServerComponents().Services
    .AddControllers().Services
    .AddDistributedMemoryCache()
    .AddSession(
        options => {
            options.IdleTimeout = TimeSpan.FromDays(30);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        }
    )
    .AddRadzenComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Error", true);
}

app.UseStaticFiles();

app.UseAntiforgery();

app.MapControllers();

app.UseSession();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();