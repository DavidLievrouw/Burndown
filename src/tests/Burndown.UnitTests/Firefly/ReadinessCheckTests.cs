using Burndown.Common;
using Burndown.Entry;
using Burndown.Firefly;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Burndown.UnitTests.Firefly;

public class ReadinessCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Error Down = new(FireflyErrorCodes.Unavailable, "Firefly is down.");

    [Fact]
    public async Task Firefly_check_is_healthy_when_about_succeeds()
    {
        var client = new FakeClient { About = Result<FireflyAbout>.Success(new FireflyAbout("6.7.8")) };

        var result = await new FireflyReadinessCheck(client).CheckHealthAsync(new HealthCheckContext(), Ct);

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Firefly_check_is_unhealthy_with_the_mapped_message_when_about_fails()
    {
        var client = new FakeClient { About = Result<FireflyAbout>.Failure(Down) };

        var result = await new FireflyReadinessCheck(client).CheckHealthAsync(new HealthCheckContext(), Ct);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe("Firefly is down.");
    }

    [Fact]
    public async Task Entry_check_is_healthy_when_cash_account_and_budgets_exist()
    {
        var result = await CheckEntry(new FakeClient());

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Entry_check_reports_a_missing_cash_account_and_missing_budgets()
    {
        var client = new FakeClient
        {
            Cash = [new FireflyAccount(AccountId.From(99), "Cash account", AccountKind.Cash, true)],
            Budgets = [new FireflyBudget(BudgetId.From(13), "David", true)],
        };

        var result = await CheckEntry(client);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        var description = result.Description.ShouldNotBeNull();
        description.ShouldContain("cash account with id 16");
        description.ShouldContain("budget 14");
        description.ShouldNotContain("budget 13");
    }

    [Fact]
    public async Task Entry_check_is_unhealthy_when_firefly_cannot_be_read()
    {
        var client = new FakeClient { CashResult = Result<IReadOnlyList<FireflyAccount>>.Failure(Down) };

        var result = await CheckEntry(client);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe("Firefly is down.");
    }

    private static Task<HealthCheckResult> CheckEntry(IFireflyClient client)
    {
        var options = new EntryOptions
        {
            CashAccountId = 16,
            Budgets = [new BudgetOption { Id = 13, Name = "David" }, new BudgetOption { Id = 14, Name = "Tina" }],
        };
        return new EntryConfigurationCheck(
            client,
            Microsoft.Extensions.Options.Options.Create(options)
        ).CheckHealthAsync(new HealthCheckContext(), Ct);
    }

    private sealed class FakeClient : IFireflyClient
    {
        public Result<FireflyAbout> About { get; init; } = Result<FireflyAbout>.Success(new FireflyAbout("6.7.8"));

        public IReadOnlyList<FireflyAccount> Cash { get; init; } =
        [new FireflyAccount(AccountId.From(16), "Cash account", AccountKind.Cash, true)];

        public Result<IReadOnlyList<FireflyAccount>>? CashResult { get; init; }

        public IReadOnlyList<FireflyBudget> Budgets { get; init; } =
        [new FireflyBudget(BudgetId.From(13), "David", true), new FireflyBudget(BudgetId.From(14), "Tina", true)];

        public Task<Result<FireflyAbout>> GetAboutAsync(CancellationToken cancellationToken) => Task.FromResult(About);

        public Task<Result<IReadOnlyList<FireflyAccount>>> GetAccountsAsync(
            AccountKind kind,
            CancellationToken cancellationToken
        ) => Task.FromResult(CashResult ?? Result<IReadOnlyList<FireflyAccount>>.Success(Cash));

        public Task<Result<IReadOnlyList<FireflyBudget>>> GetBudgetsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<FireflyBudget>>.Success(Budgets));

        public Task<Result<IReadOnlyList<string>>> GetCategoriesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<IReadOnlyList<string>>> GetTagsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<IReadOnlyList<string>>> SuggestDescriptionsAsync(
            string query,
            int limit,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<FireflyTransaction>>> GetTransactionsAsync(
            DateOnly start,
            DateOnly end,
            TransactionType? type,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<Result<CreatedTransaction>> CreateTransactionAsync(
            NewTransaction transaction,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }
}
