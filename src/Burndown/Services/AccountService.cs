using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;
using Burndown.Models;

namespace Burndown.Services;

internal class AccountService {
    private readonly AuthorizationService _authorizationService;
    private readonly HttpClient _httpClient;

    public AccountService(AuthorizationService authorizationService, HttpClient httpClient) {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _httpClient = httpClient;
    }

    public async Task<IEnumerable<string>> GetTransactionNames(string query) {
        var accessToken = GetAccessToken();

        var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/autocomplete/transactions?query=" + HttpUtility.UrlEncode(query));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode) {
            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            return doc.RootElement.EnumerateArray()
                .Select(e => e.GetProperty("name").GetString() ?? string.Empty)
                .Distinct()
                .Where(s => !string.IsNullOrEmpty(s))
                .OrderBy(s => s)
                .Take(10)
                .ToList();
        }
        
        throw new HttpRequestException("Failed to get transaction names from Firefly. " + response.ReasonPhrase);
    }

    public async Task<IEnumerable<Account>> GetAccounts() {
        var accessToken = GetAccessToken();

        var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/accounts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode) {
            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var accounts = doc.RootElement.GetProperty("data").EnumerateArray().ToList();

            return accounts
                .Where(a => a.GetProperty("attributes").GetProperty("type").GetString() == "asset")
                .Select(
                    a => new Account {
                        Id = a.GetProperty("id").GetString() ?? string.Empty,
                        Name = a.GetProperty("attributes").GetProperty("name").GetString() ?? string.Empty
                    }
                )
                .ToList();
        }

        throw new HttpRequestException("Failed to get accounts from Firefly. " + response.ReasonPhrase);
    }
    
    private string GetAccessToken() {
        if (!_authorizationService.HasToken()) throw new InvalidOperationException("No access token available.");
        return _authorizationService.GetAccessToken() ?? throw new InvalidOperationException("No access token available.");
    }
}