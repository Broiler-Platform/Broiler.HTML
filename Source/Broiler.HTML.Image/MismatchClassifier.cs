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
    /// Pure-red pixels appear in the rendered output but not the reference.
    /// Many WPT reftests follow the "passes if green, no red" convention with a
    /// <c>z-index:-1</c> red overlay that correct rendering hides behind green
    /// content; exposed red is a strong, actionable "real layout/paint bug"
    /// signal, distinct from anti-aliasing noise.
    /// </summary>
    ReferenceOverlayExposed,

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

    /// <summary>Left edge (X) of the axis-aligned box enclosing all sampled mismatches.</summary>
    public int BoundingLeft { get; init; }

    /// <summary>Top edge (Y) of the axis-aligned box enclosing all sampled mismatches.</summary>
    public int BoundingTop { get; init; }

    /// <summary>Width of the box enclosing all sampled mismatches (0 when none).</summary>
    public int BoundingWidth { get; init; }

    /// <summary>Height of the box enclosing all sampled mismatches (0 when none).</summary>
    public int BoundingHeight { get; init; }

    /// <summary>
    /// Best-effort displacement estimate describing how content moved between the
    /// reference and the output — e.g. <c>content shifted right ~100px</c>,
    /// <c>content absent</c>, or <c>extra content</c>. Null when the mismatch shows
    /// no background↔content transition to reason from (e.g. a pure colour shift).
    /// </summary>
    public string? Displacement { get; init; }

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
    /// Red channel at or above which (with low green/blue) a pixel counts as
    /// "pure red" — the WPT failure-overlay colour. Set high enough to exclude
    /// pinkish anti-aliasing fringes.
    /// </summary>
    private const byte PureRedChannelMin = 200;

    /// <summary>
    /// Green/blue channel ceiling for a pixel to count as "pure red".
    /// </summary>
    private const byte PureRedOtherChannelMax = 60;

    /// <summary>
    /// Red channel floor at or above which a red-dominant pixel counts as
    /// having legitimate red content (used to exclude reference pixels that are
    /// themselves red, e.g. a darker red, from the overlay-exposed heuristic).
    /// </summary>
    private const byte ReddishChannelMin = 64;

    /// <summary>
    /// Fraction of sampled mismatches that must be pure-red-in-output-only to
    /// classify as <see cref="MismatchCategory.ReferenceOverlayExposed"/>. Pure
    /// red never arises from anti-aliasing, so even a small fraction is a real
    /// signal; the threshold guards against an isolated stray pixel.
    /// </summary>
    private const double ReferenceOverlayFraction = 0.1;

    /// <summary>
    /// Minimum centroid offset (px) on an axis before it is reported as a content
    /// shift, so anti-aliasing/sub-pixel jitter is not described as displacement.
    /// </summary>
    private const double DisplacementThreshold = 5.0;

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
        int referenceOverlayCount = 0;

        // Bounding box of all sampled mismatches.
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

        // Centroids of background↔content transitions, used to estimate displacement:
        // "actual-only" = content in the output but blank in the reference; "baseline-only"
        // = content in the reference but blank in the output.
        long actualOnlyX = 0, actualOnlyY = 0;
        long baselineOnlyX = 0, baselineOnlyY = 0;
        int actualOnlyCount = 0, baselineOnlyCount = 0;

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

            if (m.X < minX) minX = m.X;
            if (m.X > maxX) maxX = m.X;
            if (m.Y < minY) minY = m.Y;
            if (m.Y > maxY) maxY = m.Y;

            // Detect background ↔ content transitions.
            bool actualIsWhite = m.ActualR >= WhiteThreshold &&
                                 m.ActualG >= WhiteThreshold &&
                                 m.ActualB >= WhiteThreshold;
            bool baselineIsWhite = m.BaselineR >= WhiteThreshold &&
                                   m.BaselineG >= WhiteThreshold &&
                                   m.BaselineB >= WhiteThreshold;

            if (actualIsWhite != baselineIsWhite)
                missingContentCount++;

            // Content present in the output but blank in the reference, and vice versa.
            if (!actualIsWhite && baselineIsWhite)
            {
                actualOnlyX += m.X;
                actualOnlyY += m.Y;
                actualOnlyCount++;
            }
            else if (actualIsWhite && !baselineIsWhite)
            {
                baselineOnlyX += m.X;
                baselineOnlyY += m.Y;
                baselineOnlyCount++;
            }

            // Strong red in the output where the reference has no red at all:
            // a WPT failure overlay (z-index:-1 red) showing through correct
            // content. The baseline check is deliberately loose — any reddish
            // reference pixel means red is legitimately present there, so it is
            // not an exposed overlay.
            if (IsOverlayRed(m.ActualR, m.ActualG, m.ActualB) &&
                !IsReddish(m.BaselineR, m.BaselineG, m.BaselineB))
                referenceOverlayCount++;
        }

        double avgDelta = totalDelta / mismatches.Count;
        int affectedRows = rows.Count;
        int affectedCols = cols.Count;

        // Bounding box of the mismatched region (the "dirty rect" of the diff).
        int boundingWidth = maxX >= minX ? maxX - minX + 1 : 0;
        int boundingHeight = maxY >= minY ? maxY - minY + 1 : 0;
        int boundingLeft = boundingWidth > 0 ? minX : 0;
        int boundingTop = boundingHeight > 0 ? minY : 0;

        var displacement = EstimateDisplacement(
            actualOnlyX, actualOnlyY, actualOnlyCount,
            baselineOnlyX, baselineOnlyY, baselineOnlyCount);

        // ── Classification heuristics (ordered from most specific) ─
        MismatchCategory category;
        string summary;

        // 0. Pure red exposed in the output but not the reference — the WPT
        //    "passes if green, no red" overlay showing through. Checked first:
        //    it is the most specific and most actionable signal, and pure red
        //    never arises from anti-aliasing, so it should win over the generic
        //    delta-based buckets (and over MinorDiff — a few exposed red pixels
        //    are a real bug, not a near-match).
        if (referenceOverlayCount > 0 &&
            (double)referenceOverlayCount / mismatches.Count >= ReferenceOverlayFraction)
        {
            category = MismatchCategory.ReferenceOverlayExposed;
            summary = $"{referenceOverlayCount}/{mismatches.Count} sampled mismatches are pure red in the "
                    + "output but not the reference — a red failure overlay is showing through "
                    + "(likely a real layout/paint bug).";
        }
        // 1. Very few pixels differ — near the threshold.
        else if (diff.DiffRatio < MinorDiffRatioThreshold)
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

        // Surface the displacement estimate in the human summary too (the bounding
        // box stays structured-only — it is bulky and mainly for tooling).
        if (displacement is not null)
            summary = $"{summary} {char.ToUpperInvariant(displacement[0])}{displacement[1..]}.";

        return new MismatchDiagnostics
        {
            Category = category,
            AverageChannelDelta = Math.Round(avgDelta, 2),
            MaxChannelDelta = maxDelta,
            AffectedRows = affectedRows,
            AffectedColumns = affectedCols,
            BoundingLeft = boundingLeft,
            BoundingTop = boundingTop,
            BoundingWidth = boundingWidth,
            BoundingHeight = boundingHeight,
            Displacement = displacement,
            Summary = summary,
        };
    }

    /// <summary>
    /// Estimates how content moved between the reference and the output by comparing
    /// the centroid of "content present only in the output" with that of "content
    /// present only in the reference". Returns a phrase such as
    /// <c>content shifted right ~100px</c>, <c>content absent …</c>, or
    /// <c>extra content …</c>, or <c>null</c> when there is no usable transition.
    /// </summary>
    private static string? EstimateDisplacement(
        long actualOnlyX, long actualOnlyY, int actualOnlyCount,
        long baselineOnlyX, long baselineOnlyY, int baselineOnlyCount)
    {
        bool hasActualContent = actualOnlyCount > 0;
        bool hasBaselineContent = baselineOnlyCount > 0;

        if (hasActualContent && hasBaselineContent)
        {
            double dx = (double)actualOnlyX / actualOnlyCount - (double)baselineOnlyX / baselineOnlyCount;
            double dy = (double)actualOnlyY / actualOnlyCount - (double)baselineOnlyY / baselineOnlyCount;

            var parts = new List<string>();
            if (Math.Abs(dx) >= DisplacementThreshold)
                parts.Add($"{(dx > 0 ? "right" : "left")} ~{(int)Math.Round(Math.Abs(dx))}px");
            if (Math.Abs(dy) >= DisplacementThreshold)
                parts.Add($"{(dy > 0 ? "down" : "up")} ~{(int)Math.Round(Math.Abs(dy))}px");

            // Content present on both sides but co-located → changed in place, not moved.
            return parts.Count > 0 ? "content shifted " + string.Join(" and ", parts) : null;
        }

        if (hasBaselineContent)
            return "content absent (present in reference, blank in output)";
        if (hasActualContent)
            return "extra content (present in output, blank in reference)";
        return null;
    }

    /// <summary>
    /// Whether a pixel is the WPT failure-overlay red: a strong red channel with
    /// low green/blue. The thresholds exclude pinkish anti-aliasing fringes.
    /// </summary>
    private static bool IsOverlayRed(byte r, byte g, byte b) =>
        r >= PureRedChannelMin && g <= PureRedOtherChannelMax && b <= PureRedOtherChannelMax;

    /// <summary>
    /// Whether a pixel has any meaningful red content (red is the dominant
    /// channel and above a low floor). Used to exclude reference pixels that
    /// legitimately contain red from the overlay-exposed heuristic.
    /// </summary>
    private static bool IsReddish(byte r, byte g, byte b) =>
        r >= ReddishChannelMin && r > g && r > b;
}
