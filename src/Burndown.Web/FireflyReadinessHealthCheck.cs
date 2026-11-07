using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Burndown.Web;

/// <summary>Placeholder for the Firefly connectivity check, which is implemented with the Firefly client in M1.</summary>
internal sealed class FireflyReadinessHealthCheck : IHealthCheck
{
    public const string Name = "firefly";
    public const string ReadyTag = "ready";

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(HealthCheckResult.Unhealthy("The Firefly connectivity check is not implemented yet (M1)."));
}
