﻿using System.Net.Http.Headers;
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

    public async Task<IEnumerable<Account>> GetAccounts() {
        var accessToken = GetAccessToken();

        var accountTypes = HttpUtility.UrlEncode("Asset account,Revenue account");
        var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/autocomplete/accounts?types=" + accountTypes);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode) {
            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var accounts = doc.RootElement.EnumerateArray().ToList();

            return accounts
                .Select(
                    a => new Account {
                        Id = a.GetProperty("id").GetString() ?? string.Empty,
                        Name = a.GetProperty("name").GetString() ?? string.Empty
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