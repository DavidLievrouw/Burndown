using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Burndown.Common;

namespace Burndown.Firefly;

/// <summary>Maps Firefly failures to user-readable errors. Raw bodies and reason phrases are never exposed.</summary>
internal static partial class FireflyErrors
{
    public static Error Unauthorized { get; } =
        new(
            FireflyErrorCodes.Unauthorized,
            "Firefly rejected the access token: it is invalid or has expired. Create a new personal access token."
        );

    public static Error Forbidden { get; } =
        new(FireflyErrorCodes.Forbidden, "Firefly refused the request: the access token is not allowed to do this.");

    public static Error Unavailable { get; } =
        new(FireflyErrorCodes.Unavailable, "Firefly could not be reached or reported an internal error.");

    public static Error Timeout { get; } = new(FireflyErrorCodes.Timeout, "Firefly did not answer in time.");

    public static Error Unexpected { get; } =
        new(FireflyErrorCodes.Unexpected, "Firefly answered with an unexpected response.");

    public static Error InvalidResponse { get; } =
        new(FireflyErrorCodes.InvalidResponse, "Firefly answered with data this app cannot interpret.");

    /// <summary>Maps a non-success status (other than 422, which needs its body) to an error.</summary>
    /// <param name="status">The response status code.</param>
    /// <returns>The mapped error.</returns>
    public static Error FromStatus(HttpStatusCode status) =>
        status switch
        {
            HttpStatusCode.Unauthorized => Unauthorized,
            HttpStatusCode.Forbidden => Forbidden,
            >= HttpStatusCode.InternalServerError => Unavailable,
            _ => Unexpected,
        };

    /// <summary>Builds the validation error of a 422 response from its body. An unreadable body still yields an error.</summary>
    /// <param name="body">The raw response body.</param>
    /// <returns>The validation error, with per-field messages when the body could be read.</returns>
    public static Error FromValidationBody(string body)
    {
        const string message = "Firefly rejected the submitted data.";
        Dictionary<string, string[]>? raw = null;
        try
        {
            raw = JsonSerializer.Deserialize<ValidationErrorResponse>(body, FireflyMapper.Json)?.Errors;
        }
        catch (JsonException)
        {
            // An unreadable body is reported as a validation failure without field details.
        }

        if (raw is null || raw.Count == 0)
        {
            return new Error(FireflyErrorCodes.Validation, message);
        }

        var fields = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (key, messages) in raw)
        {
            var field = SplitPrefix().Replace(key, string.Empty);
            if (!fields.TryGetValue(field, out var list))
            {
                list = [];
                fields[field] = list;
            }

            foreach (var text in messages ?? [])
            {
                if (!list.Contains(text, StringComparer.Ordinal))
                {
                    list.Add(text);
                }
            }
        }

        return new Error(
            FireflyErrorCodes.Validation,
            message,
            fields.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal)
        );
    }

    // Firefly keys field errors by their position in the request, for example "transactions.0.source_id".
    [GeneratedRegex(@"^transactions\.\d+\.", RegexOptions.CultureInvariant)]
    private static partial Regex SplitPrefix();
}
