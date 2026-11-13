using Burndown.Firefly;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Burndown.Entry;

/// <summary>Readiness check: the configured cash account and budgets exist in Firefly.</summary>
internal sealed class EntryConfigurationCheck : IHealthCheck
{
    public const string Name = "entry-configuration";

    private readonly IFireflyClient _client;
    private readonly EntryOptions _options;

    public EntryConfigurationCheck(IFireflyClient client, IOptions<EntryOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var cashAccounts = await _client.GetAccountsAsync(AccountKind.Cash, cancellationToken);
        if (!cashAccounts.IsSuccess)
        {
            return HealthCheckResult.Unhealthy(cashAccounts.Error.Message);
        }

        var budgets = await _client.GetBudgetsAsync(cancellationToken);
        if (!budgets.IsSuccess)
        {
            return HealthCheckResult.Unhealthy(budgets.Error.Message);
        }

        var problems = new List<string>();
        if (cashAccounts.Value.All(a => a.Id.Value != _options.CashAccountId))
        {
            problems.Add($"No cash account with id {_options.CashAccountId} exists in Firefly.");
        }

        var knownBudgets = budgets.Value.Select(b => b.Id.Value).ToHashSet();
        problems.AddRange(
            _options
                .Budgets.Where(b => !knownBudgets.Contains(b.Id))
                .Select(b => $"The configured budget {b.Id} ({b.Name}) does not exist in Firefly.")
        );

        return problems.Count == 0
            ? HealthCheckResult.Healthy("The configured cash account and budgets exist.")
            : HealthCheckResult.Unhealthy(string.Join(' ', problems));
    }
}
