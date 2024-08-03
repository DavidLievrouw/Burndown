using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Burndown.Models;

namespace Burndown.Services;

public class IncomeService {
    private readonly AuthorizationService _authorizationService;
    private readonly HttpClient _httpClient;

    public IncomeService(AuthorizationService authorizationService, HttpClient httpClient) {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _httpClient = httpClient;
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
    
    public async Task AddQuickIncome(QuickIncome transfer) {
        var accessToken = GetAccessToken();

        var json = $@"
        {{
          ""apply_rules"":true,
          ""fire_webhooks"":true,
          ""transactions"":[
            {{
              ""type"":""deposit"",
              ""date"":""{transfer.Date:yyyy-MM-ddTHH:mm}"",
              ""amount"":""{transfer.Amount.ToString(CultureInfo.InvariantCulture)}"",
              ""description"":""{transfer.Description}"",
              ""source_id"":""16"",
              ""destination_id"":""{transfer.Account}""
            }}
          ]
        }}";

        var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/transactions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode) {
            throw new HttpRequestException("Failed to add quick income to Firefly. " + response.ReasonPhrase);
        }
        
        if (response.Content.Headers.ContentType?.MediaType == "text/html") {
            throw new HttpRequestException("Unexpected content type: text/html.");
        }
    }

    private string GetAccessToken() {
        if (!_authorizationService.HasToken()) throw new InvalidOperationException("No access token available.");
        return _authorizationService.GetAccessToken() ?? throw new InvalidOperationException("No access token available.");
    }
}