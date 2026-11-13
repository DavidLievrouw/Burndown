using System.Net.Http.Headers;
using System.Reflection;
using Burndown.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Burndown.Firefly;

/// <summary>Registration entry point of the Firefly client module.</summary>
public static class FireflyModule
{
    internal static Assembly Assembly => typeof(FireflyModule).Assembly;

    /// <summary>
    /// Registers the Firefly options (validated at startup without the network), the typed client and the readiness
    /// check. Nothing here contacts Firefly while the app starts.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddFireflyServices(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<FireflyOptions>, FireflyOptionsValidator>();
        services.AddOptions<FireflyOptions>().BindConfiguration(FireflyOptions.Section).ValidateOnStart();

        services
            .AddHttpClient<IFireflyClient, FireflyClient>(
                (provider, client) =>
                {
                    var options = provider.GetRequiredService<IOptions<FireflyOptions>>().Value;
                    client.BaseAddress = new Uri(options.BaseUrl!.TrimEnd('/') + "/api/v1/", UriKind.Absolute);
                    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                }
            )
            .ConfigurePrimaryHttpMessageHandler(() =>
                new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) }
            );

        services.Configure<HealthCheckServiceOptions>(options =>
            options.Registrations.Add(
                new HealthCheckRegistration(
                    FireflyReadinessCheck.Name,
                    provider => ActivatorUtilities.CreateInstance<FireflyReadinessCheck>(provider),
                    failureStatus: null,
                    tags: [HealthTags.Ready]
                )
            )
        );

        return services;
    }
}
