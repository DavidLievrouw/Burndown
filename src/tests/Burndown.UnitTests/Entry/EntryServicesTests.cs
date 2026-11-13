using Burndown.Common;
using Burndown.Entry;
using Burndown.Firefly;
using Shouldly;
using static Burndown.UnitTests.Entry.EntryTestData;

namespace Burndown.UnitTests.Entry;

public class AssetAccountCacheTests
{
    private static readonly IReadOnlyList<FireflyAccount> Accounts =
    [
        new(AccountId.From(1), "Checking", AccountKind.Asset, true),
    ];

    private int _loads;

    private Task<Result<IReadOnlyList<FireflyAccount>>> Load(CancellationToken cancellationToken)
    {
        _loads++;
        return Task.FromResult(Result<IReadOnlyList<FireflyAccount>>.Success(Accounts));
    }

    [Fact]
    public async Task Serves_from_memory_within_the_time_to_live()
    {
        var (_, time) = Clock();
        var cache = new AssetAccountCache(time);

        await cache.GetAsync(Load, refresh: false, TestContext.Current.CancellationToken);
        time.Advance(AssetAccountCache.TimeToLive - TimeSpan.FromSeconds(1));
        await cache.GetAsync(Load, refresh: false, TestContext.Current.CancellationToken);

        _loads.ShouldBe(1);
    }

    [Fact]
    public async Task Reloads_after_the_time_to_live()
    {
        var (_, time) = Clock();
        var cache = new AssetAccountCache(time);

        await cache.GetAsync(Load, refresh: false, TestContext.Current.CancellationToken);
        time.Advance(AssetAccountCache.TimeToLive);
        await cache.GetAsync(Load, refresh: false, TestContext.Current.CancellationToken);

        _loads.ShouldBe(2);
    }

    [Fact]
    public async Task Refresh_bypasses_the_cache()
    {
        var (_, time) = Clock();
        var cache = new AssetAccountCache(time);

        await cache.GetAsync(Load, refresh: false, TestContext.Current.CancellationToken);
        await cache.GetAsync(Load, refresh: true, TestContext.Current.CancellationToken);

        _loads.ShouldBe(2);
    }

    [Fact]
    public async Task Does_not_cache_a_failure()
    {
        var (_, time) = Clock();
        var cache = new AssetAccountCache(time);
        var calls = 0;

        Task<Result<IReadOnlyList<FireflyAccount>>> LoadFailingOnce(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            calls++;
            return Task.FromResult(
                calls == 1
                    ? Result<IReadOnlyList<FireflyAccount>>.Failure(new Error("x", "down"))
                    : Result<IReadOnlyList<FireflyAccount>>.Success(Accounts)
            );
        }

        var first = await cache.GetAsync(LoadFailingOnce, refresh: false, TestContext.Current.CancellationToken);
        var second = await cache.GetAsync(LoadFailingOnce, refresh: false, TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeFalse();
        second.IsSuccess.ShouldBeTrue();
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task Loads_once_for_concurrent_callers()
    {
        var (_, time) = Clock();
        var cache = new AssetAccountCache(time);
        var release = new TaskCompletionSource();

        async Task<Result<IReadOnlyList<FireflyAccount>>> Slow(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _loads++;
            await release.Task;
            return Result<IReadOnlyList<FireflyAccount>>.Success(Accounts);
        }

        var a = cache.GetAsync(Slow, refresh: false, TestContext.Current.CancellationToken);
        var b = cache.GetAsync(Slow, refresh: false, TestContext.Current.CancellationToken);
        release.SetResult();
        await Task.WhenAll(a, b);

        _loads.ShouldBe(1);
    }
}

public class EntryUsageTrackerTests
{
    [Fact]
    public void Keeps_the_base_order_when_nothing_was_used()
    {
        new EntryUsageTracker().RankCategories(["A", "B", "C"]).ShouldBe(["A", "B", "C"]);
    }

    [Fact]
    public void Puts_the_most_recently_used_first_and_keeps_the_rest_in_order()
    {
        var tracker = new EntryUsageTracker();
        tracker.Record([2], "C", "t2", 14);
        tracker.Record([3], "B", "t3", null);

        tracker.RankCategories(["A", "B", "C", "D"]).ShouldBe(["B", "C", "A", "D"]);
        tracker.RankTags(["t1", "t2", "t3"]).ShouldBe(["t3", "t2", "t1"]);
        tracker
            .Rank([new AccountItem(1, "a"), new AccountItem(2, "b"), new AccountItem(3, "c")])
            .Select(a => a.Id)
            .ShouldBe([3, 2, 1]);
        tracker.Rank([new BudgetItem(13, "x"), new BudgetItem(14, "y")]).Select(b => b.Id).ShouldBe([14, 13]);
    }

    [Fact]
    public void Moves_a_value_to_the_front_when_it_is_used_again()
    {
        var tracker = new EntryUsageTracker();
        tracker.Record([], "A", null, null);
        tracker.Record([], "B", null, null);
        tracker.Record([], "A", null, null);

        tracker.RankCategories(["A", "B", "C"]).ShouldBe(["A", "B", "C"]);
        tracker.Record([], "B", null, null);
        tracker.RankCategories(["A", "B", "C"]).ShouldBe(["B", "A", "C"]);
    }
}

public class EntryReferenceServiceTests
{
    private static (EntryReferenceService Service, RecordingFireflyClient Firefly, EntryUsageTracker Usage) Create(
        string utc = "2026-05-15T10:00:00Z"
    )
    {
        var firefly = new RecordingFireflyClient();
        var (clock, time) = Clock(utc);
        var usage = new EntryUsageTracker();
        return (
            new EntryReferenceService(firefly, clock, OptionsAccessor(), usage, new AssetAccountCache(time)),
            firefly,
            usage
        );
    }

    [Fact]
    public async Task Describes_the_time_range_and_configured_lists_from_the_server_clock()
    {
        var (service, _, _) = Create("2026-05-31T22:30:00Z");

        var result = await service.GetAsync(refresh: false, TestContext.Current.CancellationToken);

        var reference = result.Value;
        reference.Today.ShouldBe(new DateOnly(2026, 6, 1));
        reference.Now.ShouldBe(new DateTimeOffset(2026, 6, 1, 0, 30, 0, TimeSpan.FromHours(2)));
        reference.TimeZone.ShouldBe("Europe/Brussels");
        reference.MinDate.ShouldBe(new DateOnly(2026, 4, 1));
        reference.MaxDate.ShouldBe(new DateOnly(2026, 6, 2));
        reference.MaxBatchSize.ShouldBe(EntrySubmitService.MaxBatchSize);
        reference.Categories.ShouldBe(["Leven", "Nodig", "Optioneel"]);
        reference.TargetTags.ShouldBe(["SharedDavid", "SharedTina", "Settled"]);
        reference.Budgets.Select(b => b.Id).ShouldBe([13, 14]);
    }

    [Fact]
    public async Task Lists_only_active_asset_accounts_sorted_by_name_ignoring_case()
    {
        var (service, _, _) = Create();

        var result = await service.GetAsync(refresh: false, TestContext.Current.CancellationToken);

        result.Value.Accounts.Select(a => a.Name).ShouldBe(["Checking", "savings"]);
    }

    [Fact]
    public async Task Ranks_recently_used_values_first()
    {
        var (service, _, usage) = Create();
        usage.Record([2], "Nodig", "Settled", 14);

        var reference = (await service.GetAsync(refresh: false, TestContext.Current.CancellationToken)).Value;

        reference.Accounts[0].Id.ShouldBe(2);
        reference.Categories[0].ShouldBe("Nodig");
        reference.TargetTags[0].ShouldBe("Settled");
        reference.Budgets[0].Id.ShouldBe(14);
    }

    [Fact]
    public async Task Caches_accounts_until_a_refresh_is_asked_for()
    {
        var (service, firefly, _) = Create();

        await service.GetAsync(refresh: false, TestContext.Current.CancellationToken);
        await service.GetAsync(refresh: false, TestContext.Current.CancellationToken);
        firefly.AccountCalls.ShouldBe(1);

        await service.GetAsync(refresh: true, TestContext.Current.CancellationToken);
        firefly.AccountCalls.ShouldBe(2);
    }

    [Fact]
    public async Task Passes_a_firefly_failure_on()
    {
        var (service, firefly, _) = Create();
        firefly.AccountsScript = () =>
            Result<IReadOnlyList<FireflyAccount>>.Failure(new Error(FireflyErrorCodes.Unauthorized, "token"));

        var result = await service.GetAsync(refresh: false, TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(FireflyErrorCodes.Unauthorized);
    }
}

public class DescriptionSuggestionServiceTests
{
    [Fact]
    public async Task Does_not_call_firefly_for_a_blank_query()
    {
        var firefly = new RecordingFireflyClient();

        var result = await new DescriptionSuggestionService(firefly).SuggestAsync(
            "  ",
            TestContext.Current.CancellationToken
        );

        result.Value.Descriptions.ShouldBeEmpty();
        firefly.Suggestions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sends_the_trimmed_query_and_returns_the_suggestions_in_order()
    {
        var firefly = new RecordingFireflyClient
        {
            SuggestScript = _ => Result<IReadOnlyList<string>>.Success(["b", "a"]),
        };

        var result = await new DescriptionSuggestionService(firefly).SuggestAsync(
            " ba ",
            TestContext.Current.CancellationToken
        );

        firefly.Suggestions.ShouldBe(["ba"]);
        result.Value.Descriptions.ShouldBe(["b", "a"]);
    }

    [Fact]
    public async Task Rejects_an_overlong_query_without_calling_firefly()
    {
        var firefly = new RecordingFireflyClient();

        var result = await new DescriptionSuggestionService(firefly).SuggestAsync(
            new string('a', 201),
            TestContext.Current.CancellationToken
        );

        result.Error.Code.ShouldBe(ErrorCodes.Validation);
        result.Error.Fields!.ShouldContainKey("q");
        firefly.Suggestions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Passes_a_firefly_failure_on()
    {
        var firefly = new RecordingFireflyClient
        {
            SuggestScript = _ => Result<IReadOnlyList<string>>.Failure(new Error(FireflyErrorCodes.Timeout, "slow")),
        };

        var result = await new DescriptionSuggestionService(firefly).SuggestAsync(
            "x",
            TestContext.Current.CancellationToken
        );

        result.Error.Code.ShouldBe(FireflyErrorCodes.Timeout);
    }
}
