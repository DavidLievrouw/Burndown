using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Burndown.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Burndown.Firefly;

/// <summary>The Firefly III API v1 client. The <see cref="HttpClient"/> carries the base address and the token.</summary>
internal sealed partial class FireflyClient : IFireflyClient
{
    public const int DefaultPageSize = 100;

    /// <summary>Guards against a server that keeps reporting more pages than any realistic data set has.</summary>
    private const int MaxPages = 1000;

    private readonly HttpClient _http;
    private readonly ILogger<FireflyClient> _logger;
    private readonly int _pageSize;

    [ActivatorUtilitiesConstructor]
    public FireflyClient(HttpClient http, ILogger<FireflyClient> logger)
        : this(http, logger, DefaultPageSize) { }

    public FireflyClient(HttpClient http, ILogger<FireflyClient> logger, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        _http = http;
        _logger = logger;
        _pageSize = pageSize;
    }

    /// <inheritdoc />
    public async Task<Result<FireflyAbout>> GetAboutAsync(CancellationToken cancellationToken)
    {
        var response = await SendAsync<AboutResponse>(HttpMethod.Get, "about", body: null, cancellationToken);
        if (!response.IsSuccess)
        {
            return Result<FireflyAbout>.Failure(response.Error);
        }

        return response.Value.Data?.Version is { Length: > 0 } version
            ? Result<FireflyAbout>.Success(new FireflyAbout(version))
            : Result<FireflyAbout>.Failure(FireflyErrors.InvalidResponse);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<FireflyAccount>>> GetAccountsAsync(
        AccountKind kind,
        CancellationToken cancellationToken
    ) =>
        GetAllAsync<AccountAttributesDto, FireflyAccount>(
            "accounts",
            "type=" + FireflyMapper.ToWire(kind),
            resource => FireflyMapper.MapAccount(resource, kind) is { } account ? [account] : null,
            cancellationToken
        );

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<string>>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        GetAllAsync<CategoryAttributesDto, string>(
            "categories",
            null,
            resource => resource.Attributes?.Name is { Length: > 0 } name ? [name] : null,
            cancellationToken
        );

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<string>>> GetTagsAsync(CancellationToken cancellationToken) =>
        GetAllAsync<TagAttributesDto, string>(
            "tags",
            null,
            resource => resource.Attributes?.Tag is { Length: > 0 } tag ? [tag] : null,
            cancellationToken
        );

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<FireflyBudget>>> GetBudgetsAsync(CancellationToken cancellationToken) =>
        GetAllAsync<BudgetAttributesDto, FireflyBudget>(
            "budgets",
            null,
            resource => FireflyMapper.MapBudget(resource) is { } budget ? [budget] : null,
            cancellationToken
        );

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> SuggestDescriptionsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var url =
            $"autocomplete/transactions?query={Uri.EscapeDataString(query)}&limit={limit.ToString(CultureInfo.InvariantCulture)}";
        var response = await SendAsync<List<AutocompleteTransactionDto>>(HttpMethod.Get, url, null, cancellationToken);
        if (!response.IsSuccess)
        {
            return Result<IReadOnlyList<string>>.Failure(response.Error);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var descriptions = new List<string>();
        foreach (var item in response.Value)
        {
            var text = item.Description ?? item.Name;
            if (!string.IsNullOrWhiteSpace(text) && seen.Add(text))
            {
                descriptions.Add(text);
            }
        }

        return Result<IReadOnlyList<string>>.Success(descriptions);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<FireflyTransaction>>> GetTransactionsAsync(
        DateOnly start,
        DateOnly end,
        TransactionType? type,
        CancellationToken cancellationToken
    )
    {
        var query =
            $"start={start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            + $"&end={end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            + $"&type={(type is { } t ? FireflyMapper.ToWire(t) : "all")}";

        return GetAllAsync<TransactionGroupAttributesDto, FireflyTransaction>(
            "transactions",
            query,
            MapGroup,
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async Task<Result<CreatedTransaction>> CreateTransactionAsync(
        NewTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var response = await SendAsync<SingleResponse<TransactionGroupAttributesDto>>(
            HttpMethod.Post,
            "transactions",
            FireflyMapper.ToRequest(transaction),
            cancellationToken
        );
        if (!response.IsSuccess)
        {
            return Result<CreatedTransaction>.Failure(response.Error);
        }

        return FireflyMapper.ParseTransactionId(response.Value.Data?.Id) is { } id
            ? Result<CreatedTransaction>.Success(new CreatedTransaction(id))
            : Result<CreatedTransaction>.Failure(FireflyErrors.InvalidResponse);
    }

    private static List<FireflyTransaction>? MapGroup(ResourceDto<TransactionGroupAttributesDto> group)
    {
        if (
            FireflyMapper.ParseTransactionId(group.Id) is not { } groupId
            || group.Attributes?.Transactions is not { } splits
        )
        {
            return null;
        }

        var result = new List<FireflyTransaction>(splits.Count);
        foreach (var split in splits)
        {
            // Opening balances, reconciliations and similar are not part of this app's world.
            if (!FireflyMapper.TryParseType(split.Type, out _))
            {
                continue;
            }

            if (FireflyMapper.MapSplit(groupId, split) is not { } mapped)
            {
                return null;
            }

            result.Add(mapped);
        }

        return result;
    }

    private async Task<Result<IReadOnlyList<TItem>>> GetAllAsync<TAttributes, TItem>(
        string path,
        string? query,
        Func<ResourceDto<TAttributes>, List<TItem>?> map,
        CancellationToken cancellationToken
    )
    {
        var items = new List<TItem>();
        var page = 1;
        while (true)
        {
            var url =
                $"{path}?{(query is null ? string.Empty : query + "&")}limit={_pageSize.ToString(CultureInfo.InvariantCulture)}&page={page.ToString(CultureInfo.InvariantCulture)}";
            var response = await SendAsync<ListResponse<TAttributes>>(HttpMethod.Get, url, null, cancellationToken);
            if (!response.IsSuccess)
            {
                return Result<IReadOnlyList<TItem>>.Failure(response.Error);
            }

            if (response.Value.Data is not { } data)
            {
                return Result<IReadOnlyList<TItem>>.Failure(FireflyErrors.InvalidResponse);
            }

            foreach (var resource in data)
            {
                if (map(resource) is not { } mapped)
                {
                    return Result<IReadOnlyList<TItem>>.Failure(FireflyErrors.InvalidResponse);
                }

                items.AddRange(mapped);
            }

            var totalPages = response.Value.Meta?.Pagination?.TotalPages ?? 1;
            if (page >= totalPages)
            {
                return Result<IReadOnlyList<TItem>>.Success(items);
            }

            if (page >= MaxPages)
            {
                return Result<IReadOnlyList<TItem>>.Failure(FireflyErrors.InvalidResponse);
            }

            page++;
        }
    }

    private async Task<Result<T>> SendAsync<T>(
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken
    )
    {
        // The query string can hold a description (personal data), so only the path is ever logged.
        var path = relativeUrl.Split('?', 2)[0];
        try
        {
            using var request = new HttpRequestMessage(method, relativeUrl);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, body.GetType(), mediaType: null, FireflyMapper.Json);
            }

            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(FireflyMapper.Json, cancellationToken);
                if (value is not null)
                {
                    return Result<T>.Success(value);
                }

                LogInvalidResponse(path);
                return Result<T>.Failure(FireflyErrors.InvalidResponse);
            }

            LogFailure(method.Method, path, (int)response.StatusCode);
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return Result<T>.Failure(FireflyErrors.FromValidationBody(errorBody));
            }

            return Result<T>.Failure(FireflyErrors.FromStatus(response.StatusCode));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimeout(method.Method, path);
            return Result<T>.Failure(FireflyErrors.Timeout);
        }
        catch (HttpRequestException exception)
        {
            LogUnreachable(method.Method, path, exception.HttpRequestError.ToString());
            return Result<T>.Failure(FireflyErrors.Unavailable);
        }
        catch (JsonException)
        {
            LogInvalidResponse(path);
            return Result<T>.Failure(FireflyErrors.InvalidResponse);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Firefly {Method} {Path} failed with status {StatusCode}.")]
    private partial void LogFailure(string method, string path, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Firefly {Method} {Path} timed out.")]
    private partial void LogTimeout(string method, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Firefly {Method} {Path} could not be reached ({Reason}).")]
    private partial void LogUnreachable(string method, string path, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Firefly {Path} returned a body that could not be interpreted.")]
    private partial void LogInvalidResponse(string path);
}
