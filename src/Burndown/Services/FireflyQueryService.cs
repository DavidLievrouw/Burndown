using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Burndown.Models;

namespace Burndown.Services;

internal class FireflyQueryService {
    private readonly AuthorizationService _authorizationService;
    private readonly HttpClient _httpClient;

    public FireflyQueryService(AuthorizationService authorizationService, HttpClient httpClient) {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _httpClient = httpClient;
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

    public async Task<decimal> GetIncomeOfPreviousMonth(DateTime selectedMonth) {
        var previousMonth = selectedMonth.AddMonths(-1);
        var start = new DateTime(previousMonth.Year, previousMonth.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        var accessToken = GetAccessToken();

        var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/transactions?start={start:yyyy-MM-dd}&end={end:yyyy-MM-dd}&limit=999&page=1&type=income");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode) {
            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var transactions = doc.RootElement.GetProperty("data").EnumerateArray().ToList();

            return transactions.Sum(
                t => decimal.Parse(
#pragma warning disable CS8604 // Possible null reference argument.
                    t.GetProperty("attributes").GetProperty("transactions")[0].GetProperty("amount").GetString(),
#pragma warning restore CS8604 // Possible null reference argument. 
                    CultureInfo.InvariantCulture
                )
            );
        }

        throw new HttpRequestException("Failed to get income from Firefly. " + response.ReasonPhrase);
    }

    public async Task<IList<Expense>> GetExpenses(DateTime selectedMonth) {
        var start = new DateTime(selectedMonth.Year, selectedMonth.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        var accessToken = GetAccessToken();

        var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/transactions?start={start:yyyy-MM-dd}&end={end:yyyy-MM-dd}&limit=999&page=1&type=withdrawal");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode) {
            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var transactions = doc.RootElement.GetProperty("data").EnumerateArray().ToList();

            return transactions
                .SelectMany(
                    t => t.GetProperty("attributes").GetProperty("transactions").EnumerateArray().Select(
                        tr => new Expense {
#pragma warning disable CS8604 // Possible null reference argument.
                            Date = DateTime.Parse(tr.GetProperty("date").GetString(), CultureInfo.InvariantCulture),
                            Description = tr.GetProperty("description").GetString(),
                            DestinationType = tr.GetProperty("destination_type").GetString(),
                            Amount = decimal.Parse(tr.GetProperty("amount").GetString(), CultureInfo.InvariantCulture),
                            Tags = tr.GetProperty("tags")
                                .EnumerateArray()
                                .Select(
                                    tag => tag.GetString() ?? string.Empty
                                )
                                .Where(tag => !string.IsNullOrEmpty(tag))
                                .ToArray()
#pragma warning restore CS8604 // Possible null reference argument.
                        }
                    )
                )
                .ToList();
        }

        throw new HttpRequestException("Failed to get expenses from Firefly. " + response.ReasonPhrase);
    }

    public async Task<decimal> GetMonthlyInstallmentsAmountOfPreviousMonth(DateTime selectedMonth) {
        var previousMonth = selectedMonth.AddMonths(-1);
        var start = new DateTime(previousMonth.Year, previousMonth.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);

        var accessToken = GetAccessToken();

        var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/transactions?start={start:yyyy-MM-dd}&end={end:yyyy-MM-dd}&limit=999&page=1&type=withdrawal");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode) {
            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var transactions = doc.RootElement.GetProperty("data").EnumerateArray().ToList();

            return transactions
                .SelectMany(
                    t => t.GetProperty("attributes").GetProperty("transactions").EnumerateArray().Select(
                        tr => {
                            return new {
#pragma warning disable CS8604 // Possible null reference argument.
                                Amount = decimal.Parse(tr.GetProperty("amount").GetString(), CultureInfo.InvariantCulture),
                                Tags = tr.GetProperty("tags").EnumerateArray().Select(
                                    tag => tag.GetString()
                                )
#pragma warning restore CS8604 // Possible null reference argument.
                            };
                        }
                    )
                )
                .Where(data => data.Tags.Contains("MonthlyInstallment"))
                .Select(data => data.Amount)
                .Sum();
        }

        throw new HttpRequestException("Failed to get monthly installments from Firefly. " + response.ReasonPhrase);
    }

    private string GetAccessToken() {
        if (!_authorizationService.HasToken()) throw new InvalidOperationException("No access token available.");
        return _authorizationService.GetAccessToken() ?? throw new InvalidOperationException("No access token available.");
    }
}