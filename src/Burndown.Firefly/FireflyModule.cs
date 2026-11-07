using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Burndown.Firefly;

/// <summary>Registration entry point of the Firefly client module.</summary>
public static class FireflyModule
{
    internal static Assembly Assembly => typeof(FireflyModule).Assembly;

    /// <summary>Registers the Firefly options. Their validation runs at startup and needs no network.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddFireflyServices(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<FireflyOptions>, FireflyOptionsValidator>();
        services.AddOptions<FireflyOptions>().BindConfiguration(FireflyOptions.Section).ValidateOnStart();
        return services;
    }
}
