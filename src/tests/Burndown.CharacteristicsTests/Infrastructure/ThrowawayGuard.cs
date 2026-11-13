namespace Burndown.CharacteristicsTests.Infrastructure;

/// <summary>
/// Write safety (PLAN.md, "Write safety"): characteristics tests post transactions, so they may only ever talk to the
/// throwaway Firefly container. The owner's real Firefly III listens on port 8042.
/// </summary>
internal static class ThrowawayGuard
{
    /// <summary>The port of the owner's real Firefly III, which tests must never address.</summary>
    internal const int RealFireflyPort = 8042;

    /// <summary>Throws unless the base URL points at a local, non-real endpoint (the container's mapped port).</summary>
    /// <param name="baseUrl">The base URL a client is about to use.</param>
    public static void Ensure(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("The throwaway Firefly URL is not an absolute URL.");
        }

        if (uri.Port == RealFireflyPort)
        {
            throw new InvalidOperationException(
                $"Refusing to use port {RealFireflyPort}: that is the owner's real Firefly III."
            );
        }

        if (!uri.IsLoopback && uri.Host != "host.docker.internal")
        {
            throw new InvalidOperationException(
                "Refusing to use a non-local Firefly URL: tests only write to the throwaway container."
            );
        }
    }

    /// <summary>Throws when an environment variable could point the tests at another Firefly.</summary>
    public static void EnsureNoEnvironmentOverrides()
    {
        foreach (var name in new[] { "Firefly__BaseUrl", "Firefly__Token" })
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
            {
                throw new InvalidOperationException(
                    $"The environment variable {name} is set. The characteristics tests use only their own container, "
                        + "so unset it to make sure that nothing can point them at a real Firefly."
                );
            }
        }
    }
}
