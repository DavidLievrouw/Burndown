namespace Burndown.Config;

public class FireflySettings {
    public required Uri BaseAddress { get; init; }
    public int ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required Uri TokenEndpoint { get; init; }
    public required Uri AuthorizationEndpoint { get; init; }
    public required Uri RedirectUri { get; init; }
}