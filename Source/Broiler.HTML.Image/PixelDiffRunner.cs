using System;
using System.Collections.Generic;
using Broiler.HTML.Core.Core.IR;

namespace Broiler.HTML.Image;

/// <summary>
/// Renders HTML deterministically and compares pixel output against baseline images (Phase 5).
/// </summary>
public static class PixelDiffRunner
{
    /// <summary>
    /// Compares two bitmaps per-pixel and returns a <see cref="PixelDiffResult"/>
    /// including a diff bitmap highlighting changed pixels.
    /// </summary>
    public static PixelDiffResult Compare(
        BBitmap actual,
        BBitmap baseline,
        DeterministicRenderConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(baseline);

        config ??= DeterministicRenderConfig.Default;

        using var normalizedActual = NormalizeForComparison(actual);
        using var normalizedBaseline = NormalizeForComparison(baseline);

        if (normalizedActual.Width != normalizedBaseline.Width || normalizedActual.Height != normalizedBaseline.Height)
        {
            return new PixelDiffResult
            {
                DiffRatio = 1.0,
                DiffPixelCount = Math.Max(normalizedActual.Width * normalizedActual.Height, normalizedBaseline.Width * normalizedBaseline.Height),
                TotalPixelCount = Math.Max(normalizedActual.Width * normalizedActual.Height, normalizedBaseline.Width * normalizedBaseline.Height),
                IsMatch = false
            };
        }

        int totalPixels = normalizedActual.Width * normalizedActual.Height;
        if (totalPixels == 0)
        {
            return new PixelDiffResult
            {
                DiffRatio = 0,
                DiffPixelCount = 0,
                TotalPixelCount = 0,
                IsMatch = true
            };
        }

        int tolerance = config.ColorTolerance;
        int diffCount = 0;
        var diffBitmap = new BBitmap(normalizedActual.Width, normalizedActual.Height);
        var mismatches = new List<PixelMismatch>();

        for (int y = 0; y < normalizedActual.Height; y++)
        {
            for (int x = 0; x < normalizedActual.Width; x++)
            {
                var p1 = normalizedActual.GetPixel(x, y);
                var p2 = normalizedBaseline.GetPixel(x, y);

                bool match = Math.Abs(p1.R - p2.R) <= tolerance &&
                             Math.Abs(p1.G - p2.G) <= tolerance &&
                             Math.Abs(p1.B - p2.B) <= tolerance &&
                             Math.Abs(p1.A - p2.A) <= tolerance;

                if (!match)
                {
                    diffCount++;
                    diffBitmap.SetPixel(x, y, new BColor(255, 0, 255, 255));

                    if (mismatches.Count < PixelDiffResult.MaxMismatchEntries)
                    {
                        mismatches.Add(new PixelMismatch(
                            x, y,
                            p1.R, p1.G, p1.B, p1.A,
                            p2.R, p2.G, p2.B, p2.A));
                    }
                }
                else
                {
                    diffBitmap.SetPixel(x, y, new BColor(
                        (byte)(p1.R / 3),
                        (byte)(p1.G / 3),
                        (byte)(p1.B / 3),
                        255));
                }
            }
        }

        double ratio = (double)diffCount / totalPixels;
        bool isMatch = ratio <= config.PixelDiffThreshold;

        if (isMatch)
        {
            diffBitmap.Dispose();
            return new PixelDiffResult
            {
                DiffRatio = ratio,
                DiffPixelCount = diffCount,
                TotalPixelCount = totalPixels,
                IsMatch = true,
                Mismatches = mismatches
            };
        }

        return new PixelDiffResult
        {
            DiffRatio = ratio,
            DiffPixelCount = diffCount,
            TotalPixelCount = totalPixels,
            DiffBitmap = diffBitmap,
            IsMatch = false,
            Mismatches = mismatches
        };
    }

    private static BBitmap NormalizeForComparison(BBitmap source)
    {
        try
        {
            return BBitmap.Decode(source.Encode(BImageFormat.Png, 100));
        }
        catch (InvalidOperationException)
        {
            return source.Copy();
        }
    }
}
