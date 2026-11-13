using Burndown.Common;
using Burndown.Firefly;

namespace Burndown.Entry;

/// <summary>
/// Caches the list of asset accounts for a short time. Only successful loads are cached, and the cache can be bypassed
/// to pick up an account that was just created in Firefly.
/// </summary>
internal sealed class AssetAccountCache
{
    /// <summary>How long a loaded list is served before it is loaded again.</summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<FireflyAccount>? _accounts;
    private DateTimeOffset _loadedAt;

    /// <summary>Initializes a new instance of the <see cref="AssetAccountCache"/> class.</summary>
    /// <param name="timeProvider">The time source used to expire the cache.</param>
    public AssetAccountCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>Gets the cached accounts, loading them when the cache is empty, expired or bypassed.</summary>
    /// <param name="load">Loads the accounts from Firefly.</param>
    /// <param name="refresh">Whether to ignore a cached list and load again.</param>
    /// <param name="cancellationToken">Cancels the wait and the load.</param>
    /// <returns>The accounts, or the failure of the load.</returns>
    public async Task<Result<IReadOnlyList<FireflyAccount>>> GetAsync(
        Func<CancellationToken, Task<Result<IReadOnlyList<FireflyAccount>>>> load,
        bool refresh,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && _accounts is { } cached && _timeProvider.GetUtcNow() - _loadedAt < TimeToLive)
            {
                return Result<IReadOnlyList<FireflyAccount>>.Success(cached);
            }

            var loaded = await load(cancellationToken);
            if (loaded.IsSuccess)
            {
                _accounts = loaded.Value;
                _loadedAt = _timeProvider.GetUtcNow();
            }

            return loaded;
        }
        finally
        {
            _gate.Release();
        }
    }
}
