using Burndown.Common;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Burndown.Firefly;

/// <summary>Readiness check: Firefly is reachable and accepts the configured token.</summary>
internal sealed class FireflyReadinessCheck : IHealthCheck
{
    public const string Name = "firefly";

    private readonly IFireflyClient _client;

    public FireflyReadinessCheck(IFireflyClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var about = await _client.GetAboutAsync(cancellationToken);
        return about.IsSuccess
            ? HealthCheckResult.Healthy($"Firefly III {about.Value.Version} is reachable.")
            : HealthCheckResult.Unhealthy(about.Error.Message);
    }
}
