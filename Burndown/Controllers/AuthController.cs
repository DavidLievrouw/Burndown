using Burndown.Services;
using Microsoft.AspNetCore.Mvc;

namespace Burndown.Controllers;

[Route("")]
[ApiController]
public class OAuthController : ControllerBase {
    private readonly AuthorizationService _authorizationService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public OAuthController(AuthorizationService authorizationService, IHttpContextAccessor httpContextAccessor) {
        _authorizationService = authorizationService;
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    [HttpGet("signin-oidc")]
    public async Task<IActionResult> Callback(string code, string state) {
        var accessToken = await _authorizationService.AcquireAccessTokenAsync(code);

        _httpContextAccessor.HttpContext?.Session.SetString("AccessToken", accessToken);

        return Redirect("/");
    }
}