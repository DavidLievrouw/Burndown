namespace Burndown.Web;

/// <summary>
/// Treats the cancellation of a request that the client itself abandoned (for example a superseded query while
/// stepping through months quickly) as a normal outcome instead of an unhandled exception that ends as a 500.
/// </summary>
internal sealed class RequestAbortedMiddleware
{
    /// <summary>The non-standard status code used for requests the client closed before the response was written.</summary>
    public const int ClientClosedRequest = 499;

    private readonly RequestDelegate _next;

    /// <summary>Initializes a new instance of the <see cref="RequestAbortedMiddleware"/> class.</summary>
    /// <param name="next">The next middleware.</param>
    public RequestAbortedMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>Runs the rest of the pipeline and absorbs cancellations caused by an aborted request.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task that completes when the request is handled.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = ClientClosedRequest;
        }
    }
}
