namespace Burndown.Common;

/// <summary>Stable error codes that are not tied to one integration.</summary>
public static class ErrorCodes
{
    /// <summary>The request was invalid. The error normally carries per-field messages.</summary>
    public const string Validation = "validation";
}
