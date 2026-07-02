namespace Broiler.HTML.Core.IR;

/// <summary>
/// Configuration for deterministic pixel-regression rendering
/// All settings are chosen to eliminate cross-platform and cross-run variation.
/// </summary>
public sealed record DeterministicRenderConfig
{
    /// <summary>Pixel-difference threshold as a ratio (0.0–1.0). Default 0.01 = 1%.</summary>
    public double PixelDiffThreshold { get; init; } = 0.01;

    /// <summary>Per-channel colour tolerance for fuzzy pixel matching (0–255).</summary>
    public int ColorTolerance { get; init; } = 5;

    public static DeterministicRenderConfig Default { get; } = new();
}
