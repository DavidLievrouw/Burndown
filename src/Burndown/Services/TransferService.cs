using System.Net.Http.Headers;
using System.Text;
using Burndown.Models;

namespace Burndown.Services;

public class TransferService {
    private readonly AuthorizationService _authorizationService;
    private readonly HttpClient _httpClient;

    public TransferService(AuthorizationService authorizationService, HttpClient httpClient) {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _httpClient = httpClient;
    }

    public async Task AddQuickTransfer(QuickTransfer transfer) {
        var accessToken = _authorizationService.GetAccessToken();

        var json = $@"
        {{
          ""apply_rules"":true,
          ""fire_webhooks"":true,
          ""transactions"":[
            {{
              ""type"":""transfer"",
              ""date"":""{transfer.Date:yyyy-MM-ddTHH:mm}"",
              ""amount"":""{transfer.Amount}"",
              ""description"":""{transfer.Description}"",
              ""source_id"":""{transfer.FromAccount}"",
              ""destination_id"":""{transfer.ToAccount}""
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