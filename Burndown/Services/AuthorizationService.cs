using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Burndown.Config;
using Microsoft.Extensions.Options;

namespace Burndown.Services;

public class AuthorizationService {
    private const string SessionKey_AccessTokenExpiration = "AccessTokenExpiration";
    private const string SessionKey_AccessToken = "AccessToken";

    private readonly IOptions<FireflySettings> _fireflyAuthSettings;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly HttpClient _httpClient;

    public AuthorizationService(IHttpContextAccessor httpContextAccessor, HttpClient httpClient, IOptions<FireflySettings> fireflySettings) {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _fireflyAuthSettings = fireflySettings ?? throw new ArgumentNullException(nameof(fireflySettings));
    }

    public string? GetAccessToken() {
        var session = _httpContextAccessor.HttpContext?.Session;
        if (session == null) {
            return null;
        }

        var expirationString = session.GetString(SessionKey_AccessTokenExpiration);
        if (string.IsNullOrEmpty(expirationString)) {
            return null;
        }
        var expirationTime = DateTime.ParseExact(expirationString, "u", CultureInfo.InvariantCulture);
        
        if (DateTime.UtcNow > expirationTime) {
            // Access token expired
            session.Remove(SessionKey_AccessToken);
            session.Remove(SessionKey_AccessTokenExpiration);
            return null;
        }
        
        return session.GetString(SessionKey_AccessToken);
    }

    public bool HasToken() {
        return !string.IsNullOrEmpty(GetAccessToken());
    }

    public Uri GetSignInUrl(string? state) {
        var fireflySettings = _fireflyAuthSettings.Value;
        return new Uri(
            $"{fireflySettings.BaseAddress.AbsoluteUri.TrimEnd('/')}/oauth/authorize?response_type=code&client_id={fireflySettings.ClientId}&redirect_uri={fireflySettings.RedirectUri}&scope=&state={state ?? string.Empty}",
            UriKind.Absolute
        );
    }

    public async Task<string> AcquireAccessTokenAsync(string? code) {
        if (string.IsNullOrEmpty(code)) throw new ArgumentException("Value cannot be null or empty.", nameof(code));

        var session = _httpContextAccessor.HttpContext?.Session;
        if (session == null) throw new InvalidOperationException("No session available.");
        
        var fireflySettings = _fireflyAuthSettings.Value;
        var request = new HttpRequestMessage(HttpMethod.Post, fireflySettings.TokenEndpoint) {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string> {
                    { "grant_type", "authorization_code" },
                    { "client_id", fireflySettings.ClientId.ToString() },
                    { "client_secret", fireflySettings.ClientSecret },
                    { "code", code },
                    { "redirect_uri", fireflySettings.RedirectUri.AbsoluteUri }
                }
            )
        };

        var response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode) {
            var responseJson = await response.Content.ReadAsStringAsync();
            var responseObject = JsonSerializer.Deserialize<TokenResponse>(responseJson);
            var newToken = responseObject?.AccessToken;

            if (string.IsNullOrEmpty(newToken)) {
                throw new HttpRequestException("Failed to get access token from Firefly. " + response.ReasonPhrase);
            }
            
            session.SetString(SessionKey_AccessToken, newToken);
            var expirationTime = DateTime.UtcNow.AddSeconds(responseObject!.ExpiresIn);
            session.SetString(SessionKey_AccessTokenExpiration, expirationTime.ToString("u", CultureInfo.InvariantCulture));
            
            return newToken;
        }

        throw new HttpRequestException("Failed to get access token from Firefly. " + response.ReasonPhrase);
    }

    private class TokenResponse {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}