using System.Reflection;
using Burndown.Common;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Burndown.Entry;

/// <summary>Registration entry point of the Entry feature slice.</summary>
public static class EntryModule
{
    internal static Assembly Assembly => typeof(EntryModule).Assembly;

    /// <summary>
    /// Registers the Entry options (validated at startup without the network) and the readiness check of the
    /// configured Firefly ids.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddEntryServices(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<EntryOptions>, EntryOptionsValidator>();
        services.AddOptions<EntryOptions>().BindConfiguration(EntryOptions.Section).ValidateOnStart();

        services.AddSingleton<EntryUsageTracker>();
        services.AddSingleton<AssetAccountCache>();
        services.AddScoped<EntryReferenceService>();
        services.AddScoped<DescriptionSuggestionService>();
        services.AddScoped<EntryRowValidator>();
        services.AddScoped<EntrySubmitService>();

        services.Configure<HealthCheckServiceOptions>(options =>
            options.Registrations.Add(
                new HealthCheckRegistration(
                    EntryConfigurationCheck.Name,
                    provider => ActivatorUtilities.CreateInstance<EntryConfigurationCheck>(provider),
                    failureStatus: null,
                    tags: [HealthTags.Ready]
                )
            )
        );

        return services;
    }

    /// <summary>Maps the Entry endpoints.</summary>
    /// <param name="routes">The route builder, normally the <c>/api</c> group.</param>
    public static void MapEntryEndpoints(this IEndpointRouteBuilder routes) => EntryEndpoints.Map(routes);
}
