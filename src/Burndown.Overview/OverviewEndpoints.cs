using Burndown.Common;
using Burndown.Firefly;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Burndown.Overview;

/// <summary>The BFF endpoints of the Overview slice.</summary>
internal static class OverviewEndpoints
{
    /// <summary>Maps <c>GET overview</c> and <c>GET overview/year</c> on the given route builder.</summary>
    /// <param name="routes">The route builder, normally the <c>/api</c> group.</param>
    public static void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet(
            "/overview",
            async (
                string? month,
                string? budgetOverride,
                OverviewService service,
                CancellationToken cancellationToken
            ) =>
            {
                var result = await service.GetAsync(month, budgetOverride, cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            }
        );

        routes.MapGet(
            "/overview/year",
            async (string? month, YearOverviewService service, CancellationToken cancellationToken) =>
            {
                var result = await service.GetAsync(month, cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            }
        );
    }

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
