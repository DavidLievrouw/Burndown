namespace Burndown.Config;

public class FireflySettings {
    public required Uri FrontChannelBaseAddress { get; init; }
    public required Uri BackChannelBaseAddress { get; init; }
    public int ClientId { get; init; }
    public required string ClientSecret { get; init; }

    public Uri AuthorizationEndpoint =>
        new($"{FrontChannelBaseAddress.AbsoluteUri.TrimEnd('/')}/oauth/authorize", UriKind.Absolute);

    public Uri TokenEndpoint =>
        new($"{BackChannelBaseAddress.AbsoluteUri.TrimEnd('/')}/oauth/token", UriKind.Absolute);

    public required Uri RedirectUri { get; init; }
}