using System;

namespace Broiler.HTML.Image;

/// <summary>
/// Identifies the active graphics backend behind the Broiler-owned image
/// abstractions.
/// </summary>
public static class BGraphicsBackend
{
    internal const string BroilerRasterId = "broiler";

    /// <summary>
    /// Stable machine-readable identifier for the current backend.
    /// </summary>
    public static string CurrentId => ResolveCurrent().Id;

    /// <summary>
    /// Human-readable name for the current backend implementation.
    /// </summary>
    public static string CurrentDisplayName => ResolveCurrent().DisplayName;

    /// <summary>
    /// Human-readable combined backend label for diagnostics and artifacts.
    /// </summary>
    public static string CurrentLabel => $"{CurrentId} ({CurrentDisplayName})";

    internal static bool UseBroilerRasterPipeline => string.Equals(CurrentId, BroilerRasterId, StringComparison.Ordinal);

    private static BackendDefinition ResolveCurrent()
    {
        return new BackendDefinition(BroilerRasterId, "Broiler raster");
    }

    private readonly record struct BackendDefinition(string Id, string DisplayName);
}
