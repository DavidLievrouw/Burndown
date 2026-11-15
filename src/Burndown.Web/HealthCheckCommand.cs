using System.Net;
using System.Text.RegularExpressions;

namespace Burndown.Web;

/// <summary>The <c>--health-check</c> switch used by the container healthcheck: it calls <c>/health/live</c> locally.</summary>
internal static partial class HealthCheckCommand
{
    public const string Switch = "--health-check";
    public const int DefaultPort = 9042;
    public const string LivePath = "/health/live";

    /// <summary>Determines the port the running instance listens on from <c>ASPNETCORE_URLS</c>.</summary>
    /// <param name="urls">The value of <c>ASPNETCORE_URLS</c>, or <see langword="null"/> when it is not set.</param>
    /// <returns>The first port found, or <see cref="DefaultPort"/>.</returns>
    public static int ResolvePort(string? urls)
    {
        if (urls is not null)
        {
            foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var match = PortPattern().Match(url);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var port) && port is > 0 and <= 65535)
                {
                    return port;
                }
            }
        }

        return DefaultPort;
    }

    /// <summary>Calls the live endpoint of the local instance.</summary>
    /// <param name="client">The HTTP client to use.</param>
    /// <param name="urls">The value of <c>ASPNETCORE_URLS</c>, or <see langword="null"/> when it is not set.</param>
    /// <param name="error">Receives a one-line diagnostic when the check fails.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Process exit code: 0 when the instance is alive, 1 otherwise.</returns>
    public static async Task<int> RunAsync(
        HttpClient client,
        string? urls,
        TextWriter error,
        CancellationToken cancellationToken = default
    )
    {
        var port = ResolvePort(urls);
        try
        {
            using var response = await client.GetAsync(
                new Uri($"http://{IPAddress.Loopback}:{port}{LivePath}"),
                cancellationToken
            );
            if (response.IsSuccessStatusCode)
            {
                return 0;
            }

            await error.WriteLineAsync($"Health endpoint returned HTTP {(int)response.StatusCode}.");
            return 1;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            await error.WriteLineAsync(
                $"Could not reach the health endpoint on port {port}: {exception.GetType().Name}."
            );
            return 1;
        }
    }

    [GeneratedRegex(@":(\d{1,5})(?:/.*)?$")]
    private static partial Regex PortPattern();
}
