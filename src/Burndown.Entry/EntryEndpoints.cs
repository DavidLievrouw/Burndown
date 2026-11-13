using Burndown.Common;
using Burndown.Firefly;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Burndown.Entry;

/// <summary>The BFF endpoints of the Entry slice.</summary>
internal static class EntryEndpoints
{
    /// <summary>Maps the Entry endpoints on the given route builder.</summary>
    /// <param name="routes">The route builder, normally the <c>/api</c> group.</param>
    public static void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet(
            "/entry/reference",
            async (EntryReferenceService service, CancellationToken cancellationToken) =>
                ToResult(await service.GetAsync(refresh: false, cancellationToken))
        );

        routes.MapPost(
            "/entry/reference/refresh",
            async (EntryReferenceService service, CancellationToken cancellationToken) =>
                ToResult(await service.GetAsync(refresh: true, cancellationToken))
        );

        routes.MapGet(
            "/entry/suggest/descriptions",
            async (string? q, DescriptionSuggestionService service, CancellationToken cancellationToken) =>
                ToResult(await service.SuggestAsync(q, cancellationToken))
        );

        routes.MapPost(
            "/entry/submit",
            async (SubmitRequest? request, EntrySubmitService service, CancellationToken cancellationToken) =>
                ToResult(await service.SubmitAsync(request, cancellationToken))
        );
    }

    private static IResult ToResult<T>(Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);

    private static IResult ToProblem(Error error)
    {
        var status = error.Code == ErrorCodes.Validation ? 400 : FireflyErrorCodes.ToHttpStatusCode(error.Code) ?? 500;
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
        if (error.Fields is not null)
        {
            extensions["errors"] = error.Fields;
        }

        return Results.Problem(detail: error.Message, statusCode: status, extensions: extensions);
    }
}
