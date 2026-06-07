using System;
using System.Threading;

namespace Broiler.HTML.Image;

/// <summary>
/// Identifies the active graphics backend behind the Broiler-owned image
/// abstractions.
/// </summary>
public static class BGraphicsBackend
{
    internal const string BroilerRasterId = "broiler";
    internal const string GdiFallbackId = "gdi";

    private static readonly AsyncLocal<string?> BackendOverride = new();

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

    internal static IDisposable OverrideForCurrentThread(string? backendId)
    {
        var previous = BackendOverride.Value;
        BackendOverride.Value = backendId;
        return new BackendOverrideScope(previous);
    }

    private static BackendDefinition ResolveCurrent()
    {
        return Resolve(BackendOverride.Value);
    }

    private static BackendDefinition Resolve(string? configuredBackend) =>
        string.Equals(configuredBackend, GdiFallbackId, StringComparison.OrdinalIgnoreCase)
            ? new BackendDefinition(GdiFallbackId, "GDI+ compatibility fallback")
            : new BackendDefinition(BroilerRasterId, "Broiler raster");

    private readonly record struct BackendDefinition(string Id, string DisplayName);

    private sealed class BackendOverrideScope(string? previous) : IDisposable
    {
        public void Dispose() => BackendOverride.Value = previous;
    }
}
