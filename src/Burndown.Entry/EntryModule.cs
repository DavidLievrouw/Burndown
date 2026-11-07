using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Burndown.Entry;

/// <summary>Registration entry point of the Entry feature slice.</summary>
public static class EntryModule
{
    internal static Assembly Assembly => typeof(EntryModule).Assembly;

    /// <summary>Registers the Entry options. Their validation runs at startup and needs no network.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddEntryServices(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<EntryOptions>, EntryOptionsValidator>();
        services.AddOptions<EntryOptions>().BindConfiguration(EntryOptions.Section).ValidateOnStart();
        return services;
    }
}
