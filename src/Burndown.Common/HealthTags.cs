namespace Burndown.Common;

/// <summary>Tags that select which health checks run for which health endpoint.</summary>
public static class HealthTags
{
    /// <summary>Checks that decide whether the app can do its work (reaches Firefly, is configured correctly).</summary>
    public const string Ready = "ready";
}
