using Burndown.Web;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace Burndown.UnitTests.Web;

public sealed class RequestAbortedMiddlewareTests
{
    [Fact]
    public async Task A_cancellation_of_an_aborted_request_ends_as_a_client_closed_request()
    {
        using var aborted = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };
        var middleware = new RequestAbortedMiddleware(async _ =>
        {
            await aborted.CancelAsync();
            throw new TaskCanceledException();
        });

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(RequestAbortedMiddleware.ClientClosedRequest);
    }

    [Fact]
    public async Task A_cancellation_of_a_live_request_is_not_swallowed()
    {
        var context = new DefaultHttpContext();
        var middleware = new RequestAbortedMiddleware(_ => throw new TaskCanceledException());

        await Should.ThrowAsync<TaskCanceledException>(() => middleware.InvokeAsync(context));
    }

    [Fact]
    public async Task Other_exceptions_are_not_swallowed()
    {
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };
        var middleware = new RequestAbortedMiddleware(_ => throw new InvalidOperationException());

        await Should.ThrowAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
    }
}
