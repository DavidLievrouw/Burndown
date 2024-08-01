using System.Net.Http.Headers;
using System.Text;
using Burndown.Models;

namespace Burndown.Services;

public class ExpenseService {
    private readonly AuthorizationService _authorizationService;
    private readonly HttpClient _httpClient;

    public ExpenseService(AuthorizationService authorizationService, HttpClient httpClient) {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _httpClient = httpClient;
    }

    public async Task AddQuickExpense(QuickExpense expense) {
        var accessToken = _authorizationService.GetAccessToken();

        var json = $@"
        {{
            ""apply_rules"": true,
            ""fire_webhooks"": true,
            ""transactions"": [
                {{
                    ""type"": ""withdrawal"",
                    ""date"": ""{expense.Date:yyyy-MM-ddTHH:mm}"",
                    ""amount"": ""{expense.Amount}"",
                    ""description"": ""{expense.Description}"",
                    ""source_id"": ""{expense.Account}"",
                    ""destination_id"": ""16"",
                    ""category_name"": ""{expense.Category}"",
                    ""tags"": [
                        ""{expense.Target}""
                    ]
                }}
            ]
        }}";

        var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/transactions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode) {
            throw new HttpRequestException("Failed to add quick expense to Firefly. " + response.ReasonPhrase);
        }
    }
}