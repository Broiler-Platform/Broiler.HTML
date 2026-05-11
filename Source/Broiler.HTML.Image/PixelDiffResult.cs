using System;
using System.Collections.Generic;

namespace Broiler.HTML.Image;

/// <summary>
/// Records a single pixel-level colour mismatch between two rendered images.
/// </summary>
/// <param name="X">Horizontal pixel position (0-based).</param>
/// <param name="Y">Vertical pixel position (0-based).</param>
/// <param name="ActualR">Red channel of the actual (Broiler) pixel.</param>
/// <param name="ActualG">Green channel of the actual (Broiler) pixel.</param>
/// <param name="ActualB">Blue channel of the actual (Broiler) pixel.</param>
/// <param name="ActualA">Alpha channel of the actual (Broiler) pixel.</param>
/// <param name="BaselineR">Red channel of the baseline (Chromium) pixel.</param>
/// <param name="BaselineG">Green channel of the baseline (Chromium) pixel.</param>
/// <param name="BaselineB">Blue channel of the baseline (Chromium) pixel.</param>
/// <param name="BaselineA">Alpha channel of the baseline (Chromium) pixel.</param>
public readonly record struct PixelMismatch(
    int X, int Y,
    byte ActualR, byte ActualG, byte ActualB, byte ActualA,
    byte BaselineR, byte BaselineG, byte BaselineB, byte BaselineA);

public sealed class PixelDiffResult : IDisposable
{
    /// <summary>
    /// Maximum number of individual <see cref="PixelMismatch"/> entries
    /// collected per comparison.  Prevents excessive memory usage when
    /// images differ significantly.
    /// </summary>
    public const int MaxMismatchEntries = 10_000;

    /// <summary>Ratio of differing pixels (0.0 = identical, 1.0 = every pixel differs).</summary>
    public double DiffRatio { get; init; }

    public int DiffPixelCount { get; init; }

    public int TotalPixelCount { get; init; }

    /// <summary>
    /// Diff bitmap highlighting changed pixels in magenta.
    /// Null when images are identical or have different dimensions.
    /// Caller is responsible for disposal via <see cref="Dispose"/>.
    /// </summary>
    public BBitmap? DiffBitmap { get; init; }

    /// <summary>Whether the images are considered matching (diff ≤ threshold).</summary>
    public bool IsMatch { get; init; }

    /// <summary>
    /// Per-pixel mismatch entries (position + colours for both images).
    /// Capped at <see cref="MaxMismatchEntries"/> to limit memory usage.
    /// Empty when images are identical or have different dimensions.
    /// </summary>
    public IReadOnlyList<PixelMismatch> Mismatches { get; init; } = [];

    public void Dispose() => DiffBitmap?.Dispose();
}
