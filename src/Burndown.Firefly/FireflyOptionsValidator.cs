using Microsoft.Extensions.Options;

namespace Burndown.Firefly;

/// <summary>Validates <see cref="FireflyOptions"/> without touching the network.</summary>
internal sealed class FireflyOptionsValidator : IValidateOptions<FireflyOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FireflyOptions options)
    {
        var failures = new List<string>();

        if (
            !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl)
            || baseUrl.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(baseUrl.UserInfo)
        )
        {
            failures.Add(
                $"{FireflyOptions.Section}:{nameof(FireflyOptions.BaseUrl)} must be an absolute http(s) URL without credentials."
            );
        }

        if (string.IsNullOrWhiteSpace(options.Token))
        {
            failures.Add(
                $"{FireflyOptions.Section}:{nameof(FireflyOptions.Token)} is required "
                    + "(set it with user-secrets or the Firefly__Token environment variable)."
            );
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
