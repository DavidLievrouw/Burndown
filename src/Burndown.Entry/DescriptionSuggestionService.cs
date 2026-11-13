using Burndown.Common;
using Burndown.Firefly;

namespace Burndown.Entry;

/// <summary>Suggests descriptions of earlier transactions through Firefly's autocomplete, used as-is.</summary>
internal sealed class DescriptionSuggestionService
{
    /// <summary>The longest query that is accepted.</summary>
    public const int MaxQueryLength = 200;

    /// <summary>The number of suggestions asked from Firefly.</summary>
    public const int Limit = 10;

    private readonly IFireflyClient _firefly;

    /// <summary>Initializes a new instance of the <see cref="DescriptionSuggestionService"/> class.</summary>
    /// <param name="firefly">The Firefly client.</param>
    public DescriptionSuggestionService(IFireflyClient firefly)
    {
        _firefly = firefly;
    }

    /// <summary>Gets the suggestions for the text typed so far.</summary>
    /// <param name="query">The text typed so far. Blank text yields no suggestions and no Firefly call.</param>
    /// <param name="cancellationToken">Cancels the call, for example when the user types on.</param>
    /// <returns>The suggestions in Firefly's order, or the validation or Firefly failure.</returns>
    public async Task<Result<DescriptionSuggestions>> SuggestAsync(string? query, CancellationToken cancellationToken)
    {
        var text = query?.Trim() ?? string.Empty;
        if (text.Length > MaxQueryLength)
        {
            return Result<DescriptionSuggestions>.Failure(
                new Error(
                    ErrorCodes.Validation,
                    "The query is too long.",
                    new Dictionary<string, string[]>
                    {
                        ["q"] = [$"The query can have at most {MaxQueryLength} characters."],
                    }
                )
            );
        }

        if (text.Length == 0)
        {
            return Result<DescriptionSuggestions>.Success(new DescriptionSuggestions([]));
        }

        var result = await _firefly.SuggestDescriptionsAsync(text, Limit, cancellationToken);
        return result.IsSuccess
            ? Result<DescriptionSuggestions>.Success(new DescriptionSuggestions(result.Value))
            : Result<DescriptionSuggestions>.Failure(result.Error);
    }
}
