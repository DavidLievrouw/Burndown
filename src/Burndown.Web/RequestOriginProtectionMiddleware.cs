using System.Net.Http.Headers;

namespace Burndown.Web;

/// <summary>
/// Protects the loopback-only app against cross-site and DNS-rebinding attacks: state-changing requests must be JSON and
/// must come from the app's own loopback origin (or carry no Origin header at all, as non-browser clients do).
/// </summary>
internal sealed class RequestOriginProtectionMiddleware
{
    private static readonly string[] SafeMethods = ["GET", "HEAD", "OPTIONS", "TRACE"];

    private readonly RequestDelegate _next;
    private readonly string[] _additionalOrigins;

    /// <summary>Initializes a new instance of the <see cref="RequestOriginProtectionMiddleware"/> class.</summary>
    /// <param name="next">The next middleware.</param>
    /// <param name="additionalOrigins">Extra allowed origins, for example the Vite dev server in Development.</param>
    public RequestOriginProtectionMiddleware(RequestDelegate next, string[] additionalOrigins)
    {
        _next = next;
        _additionalOrigins = additionalOrigins;
    }

    /// <summary>Applies the origin and content-type rules.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task that completes when the request is handled.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (SafeMethods.Contains(request.Method, StringComparer.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (request.Headers.TryGetValue("Origin", out var origin) && !IsAllowedOrigin(origin.ToString(), request))
        {
            await Results
                .Problem(statusCode: StatusCodes.Status403Forbidden, title: "Origin not allowed.")
                .ExecuteAsync(context);
            return;
        }

        if (
            !MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            || !string.Equals(contentType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)
        )
        {
            await Results
                .Problem(
                    statusCode: StatusCodes.Status415UnsupportedMediaType,
                    title: "Content-Type must be application/json."
                )
                .ExecuteAsync(context);
            return;
        }

        await _next(context);
    }

    private bool IsAllowedOrigin(string origin, HttpRequest request)
    {
        // Browsers omit the port from the Origin header when it is the default one.
        var port = request.Host.Port ?? 80;
        var suffix = port == 80 ? string.Empty : $":{port}";
        return string.Equals(origin, $"http://localhost{suffix}", StringComparison.OrdinalIgnoreCase)
            || string.Equals(origin, $"http://127.0.0.1{suffix}", StringComparison.OrdinalIgnoreCase)
            || _additionalOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }
}
