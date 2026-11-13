using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Burndown.Overview;

/// <summary>Registration entry point of the Overview feature slice.</summary>
public static class OverviewModule
{
    internal static Assembly Assembly => typeof(OverviewModule).Assembly;

    /// <summary>Registers the Overview options. Their validation runs at startup and needs no network.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddOverviewServices(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<OverviewOptions>, OverviewOptionsValidator>();
        services.AddOptions<OverviewOptions>().BindConfiguration(OverviewOptions.Section).ValidateOnStart();
        services.AddScoped<OverviewService>();
        return services;
    }

    /// <summary>Maps the Overview endpoints.</summary>
    /// <param name="routes">The route builder, normally the <c>/api</c> group.</param>
    public static void MapOverviewEndpoints(this IEndpointRouteBuilder routes) => OverviewEndpoints.Map(routes);
}
