namespace Burndown.Firefly;

/// <summary>Connection settings for the Firefly III API, bound from the <c>Firefly</c> configuration section.</summary>
internal sealed class FireflyOptions
{
    public const string Section = "Firefly";

    /// <summary>Gets or sets the base URL of the Firefly III instance, without the <c>/api/v1</c> suffix.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Gets or sets the personal access token. This is a secret: it must never be logged.</summary>
    public string? Token { get; set; }
}
