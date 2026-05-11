using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.HTML.Image;

/// <summary>
/// Sub-classifies the root cause of a pixel mismatch between rendered
/// output and a reference image.
/// </summary>
public enum MismatchCategory
{
    /// <summary>Images have different dimensions.</summary>
    SizeMismatch,

    /// <summary>
    /// Small per-channel differences near the tolerance boundary, typically
    /// caused by anti-aliasing or sub-pixel rendering differences.
    /// </summary>
    SubpixelAntiAliasing,

    /// <summary>
    /// Pervasive low-to-moderate color shift spread across many pixels
    /// (e.g. gamma, color-space, or blending differences).
    /// </summary>
    ColorShift,

    /// <summary>
    /// Large contiguous regions of difference suggesting a structural or
    /// layout change (element repositioning, sizing, or flow differences).
    /// </summary>
    LayoutShift,

    /// <summary>
    /// Content that is present in one image but absent (white/transparent
    /// background) in the other, indicating missing or extra elements.
    /// </summary>
    MissingContent,

    /// <summary>
    /// Very few pixels differ — near the pass/fail threshold.
    /// </summary>
    MinorDiff,
}

/// <summary>
/// Structured diagnostics describing a pixel-mismatch failure.
/// </summary>
public sealed class MismatchDiagnostics
{
    /// <summary>Sub-category of the mismatch.</summary>
    public MismatchCategory Category { get; init; }

    /// <summary>Average per-channel delta across all sampled mismatches.</summary>
    public double AverageChannelDelta { get; init; }

    /// <summary>Maximum single-channel delta observed.</summary>
    public int MaxChannelDelta { get; init; }

    /// <summary>Number of distinct rows containing at least one mismatch.</summary>
    public int AffectedRows { get; init; }

    /// <summary>Number of distinct columns containing at least one mismatch.</summary>
    public int AffectedColumns { get; init; }

    /// <summary>Human-readable one-line summary of the classification.</summary>
    public required string Summary { get; init; }
}

/// <summary>
/// Analyses a <see cref="PixelDiffResult"/> and classifies the mismatch
/// into an actionable <see cref="MismatchCategory"/> with diagnostics.
/// </summary>
public static class MismatchClassifier
{
    /// <summary>
    /// Per-channel delta at or below which a mismatch is considered an
    /// anti-aliasing or sub-pixel artefact.
    /// </summary>
    private const double AntiAliasingDeltaThreshold = 25.0;

    /// <summary>
    /// Per-channel delta at or below which a mismatch is considered a
    /// colour shift rather than a structural difference.
    /// </summary>
    private const double ColorShiftDeltaThreshold = 80.0;

    /// <summary>
    /// Diff ratio below which the failure is considered a minor diff.
    /// </summary>
    private const double MinorDiffRatioThreshold = 0.01;

    /// <summary>
    /// Channel value above which a pixel is considered "near-white"
    /// (background).
    /// </summary>
    private const byte WhiteThreshold = 240;

    /// <summary>
    /// Fraction of mismatches that must be background-to-content (or
    /// vice-versa) transitions to classify as <see cref="MismatchCategory.MissingContent"/>.
    /// </summary>
    private const double MissingContentFraction = 0.5;

    /// <summary>
    /// Classifies a pixel-diff result into a <see cref="MismatchCategory"/>
    /// and produces a <see cref="MismatchDiagnostics"/> report.
    /// </summary>
    /// <param name="diff">
    /// The pixel-diff result to classify.  Must represent a failed comparison
    /// (<see cref="PixelDiffResult.IsMatch"/> == false).
    /// </param>
    /// <param name="actualWidth">Width of the actual (rendered) image.</param>
    /// <param name="actualHeight">Height of the actual (rendered) image.</param>
    /// <param name="baselineWidth">Width of the baseline (reference) image.</param>
    /// <param name="baselineHeight">Height of the baseline (reference) image.</param>
    public static MismatchDiagnostics Classify(
        PixelDiffResult diff,
        int actualWidth, int actualHeight,
        int baselineWidth, int baselineHeight)
    {
        // ── Size mismatch ──────────────────────────────────────────
        if (actualWidth != baselineWidth || actualHeight != baselineHeight)
        {
            return new MismatchDiagnostics
            {
                Category = MismatchCategory.SizeMismatch,
                AverageChannelDelta = 0,
                MaxChannelDelta = 0,
                AffectedRows = 0,
                AffectedColumns = 0,
                Summary = $"Image dimensions differ: actual {actualWidth}×{actualHeight} vs baseline {baselineWidth}×{baselineHeight}.",
            };
        }

        var mismatches = diff.Mismatches;

        // If no mismatch samples are available (e.g. empty images or
        // zero-area bitmaps) fall back to minor diff.
        if (mismatches.Count == 0)
        {
            return new MismatchDiagnostics
            {
                Category = MismatchCategory.MinorDiff,
                AverageChannelDelta = 0,
                MaxChannelDelta = 0,
                AffectedRows = 0,
                AffectedColumns = 0,
                Summary = "No mismatch samples available.",
            };
        }

        // ── Compute aggregate metrics ──────────────────────────────
        double totalDelta = 0;
        int maxDelta = 0;
        var rows = new HashSet<int>();
        var cols = new HashSet<int>();
        int missingContentCount = 0;

        foreach (var m in mismatches)
        {
            int dR = Math.Abs(m.ActualR - m.BaselineR);
            int dG = Math.Abs(m.ActualG - m.BaselineG);
            int dB = Math.Abs(m.ActualB - m.BaselineB);
            int dA = Math.Abs(m.ActualA - m.BaselineA);

            double avg = (dR + dG + dB + dA) / 4.0;
            totalDelta += avg;

            int channelMax = Math.Max(Math.Max(dR, dG), Math.Max(dB, dA));
            if (channelMax > maxDelta)
                maxDelta = channelMax;

            rows.Add(m.Y);
            cols.Add(m.X);

            // Detect background ↔ content transitions.
            bool actualIsWhite = m.ActualR >= WhiteThreshold &&
                                 m.ActualG >= WhiteThreshold &&
                                 m.ActualB >= WhiteThreshold;
            bool baselineIsWhite = m.BaselineR >= WhiteThreshold &&
                                   m.BaselineG >= WhiteThreshold &&
                                   m.BaselineB >= WhiteThreshold;

            if (actualIsWhite != baselineIsWhite)
                missingContentCount++;
        }

        double avgDelta = totalDelta / mismatches.Count;
        int affectedRows = rows.Count;
        int affectedCols = cols.Count;

        // ── Classification heuristics (ordered from most specific) ─
        MismatchCategory category;
        string summary;

        // 1. Very few pixels differ — near the threshold.
        if (diff.DiffRatio < MinorDiffRatioThreshold)
        {
            category = MismatchCategory.MinorDiff;
            summary = $"Near-match: only {diff.DiffPixelCount} pixel(s) differ "
                    + $"({diff.DiffRatio * 100:F2}%), avg Δ {avgDelta:F1}.";
        }
        // 2. Majority of differences are white ↔ non-white transitions.
        else if ((double)missingContentCount / mismatches.Count >= MissingContentFraction)
        {
            category = MismatchCategory.MissingContent;
            summary = $"{missingContentCount}/{mismatches.Count} sampled mismatches "
                    + "are background↔content transitions, indicating missing or extra elements.";
        }
        // 3. Small deltas — anti-aliasing / sub-pixel rendering.
        else if (avgDelta <= AntiAliasingDeltaThreshold)
        {
            category = MismatchCategory.SubpixelAntiAliasing;
            summary = $"Low avg channel delta ({avgDelta:F1}); likely anti-aliasing or sub-pixel rendering differences.";
        }
        // 4. Moderate deltas spread widely — colour shift.
        else if (avgDelta <= ColorShiftDeltaThreshold)
        {
            category = MismatchCategory.ColorShift;
            summary = $"Moderate avg channel delta ({avgDelta:F1}) across {affectedRows} row(s); "
                    + "likely a color or blending difference.";
        }
        // 5. Default — significant structural / layout shift.
        else
        {
            category = MismatchCategory.LayoutShift;
            summary = $"High avg channel delta ({avgDelta:F1}) across {affectedRows} row(s) "
                    + $"and {affectedCols} column(s); likely a layout or structural change.";
        }

        return new MismatchDiagnostics
        {
            Category = category,
            AverageChannelDelta = Math.Round(avgDelta, 2),
            MaxChannelDelta = maxDelta,
            AffectedRows = affectedRows,
            AffectedColumns = affectedCols,
            Summary = summary,
        };
    }
}
