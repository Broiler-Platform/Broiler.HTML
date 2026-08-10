using System;
using System.Collections.Generic;
using Broiler.HTML.Core.IR;
using Broiler.Graphics;
using Broiler.Media.Image;

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

        // Compared directly. This used to round-trip both inputs through
        // `BBitmap.Decode(source.Encode(Png, 100))` before looking at a single pixel, which cost
        // ~141 ms per bitmap — 99% of a 1024x768 comparison, and 16-21% of a whole WPT run — for a
        // transformation measured to be an identity on pixel values (see the header comment on
        // NormalizeForComparison below for what replaced it and how that was established).
        var normalizedActual = actual;
        var normalizedBaseline = baseline;

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
        var mismatches = new List<PixelMismatch>();

        // Pass 1 counts, and allocates nothing. The diff bitmap is built in pass 2 and only when
        // the comparison actually failed: it used to be allocated and written for every pixel of
        // every comparison, then thrown away on the match path — a 3 MB image and 786 432
        // SetPixel calls that nothing ever looked at, on the ~62% of WPT tests that pass.
        //
        // Both bitmaps are read through their backing spans rather than GetPixel, which is the
        // same store GetPixel reads (BBitmap.PixelBytes) without a call and a BColor per pixel.
        var actualBytes = normalizedActual.PixelBytes;
        var baselineBytes = normalizedBaseline.PixelBytes;
        int width = normalizedActual.Width;

        for (int y = 0; y < normalizedActual.Height; y++)
        {
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int i = row + x * 4;

                bool match = Math.Abs(actualBytes[i] - baselineBytes[i]) <= tolerance &&
                             Math.Abs(actualBytes[i + 1] - baselineBytes[i + 1]) <= tolerance &&
                             Math.Abs(actualBytes[i + 2] - baselineBytes[i + 2]) <= tolerance &&
                             Math.Abs(actualBytes[i + 3] - baselineBytes[i + 3]) <= tolerance;

                if (!match)
                {
                    diffCount++;

                    if (mismatches.Count < PixelDiffResult.MaxMismatchEntries)
                    {
                        mismatches.Add(new PixelMismatch(
                            x, y,
                            actualBytes[i], actualBytes[i + 1], actualBytes[i + 2], actualBytes[i + 3],
                            baselineBytes[i], baselineBytes[i + 1], baselineBytes[i + 2], baselineBytes[i + 3]));
                    }
                }
            }
        }

        double ratio = (double)diffCount / totalPixels;
        bool isMatch = ratio <= config.PixelDiffThreshold;

        if (isMatch)
        {
            return new PixelDiffResult
            {
                DiffRatio = ratio,
                DiffPixelCount = diffCount,
                TotalPixelCount = totalPixels,
                IsMatch = true,
                Mismatches = mismatches
            };
        }

        var diffBitmap = BuildDiffBitmap(actualBytes, baselineBytes, width, normalizedActual.Height, tolerance);

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

    /// <summary>
    /// The failure-path diff image: magenta where the two differ, the actual dimmed to a third
    /// where they agree. Byte for byte what the single-pass version produced — only later, and only
    /// when someone is going to look at it.
    /// </summary>
    private static BBitmap BuildDiffBitmap(
        ReadOnlySpan<byte> actualBytes, ReadOnlySpan<byte> baselineBytes, int width, int height, int tolerance)
    {
        var pixels = new byte[checked(width * height * 4)];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            bool match = Math.Abs(actualBytes[i] - baselineBytes[i]) <= tolerance &&
                         Math.Abs(actualBytes[i + 1] - baselineBytes[i + 1]) <= tolerance &&
                         Math.Abs(actualBytes[i + 2] - baselineBytes[i + 2]) <= tolerance &&
                         Math.Abs(actualBytes[i + 3] - baselineBytes[i + 3]) <= tolerance;

            if (!match)
            {
                pixels[i] = 255;
                pixels[i + 1] = 0;
                pixels[i + 2] = 255;
            }
            else
            {
                pixels[i] = (byte)(actualBytes[i] / 3);
                pixels[i + 1] = (byte)(actualBytes[i + 1] / 3);
                pixels[i + 2] = (byte)(actualBytes[i + 2] / 3);
            }

            pixels[i + 3] = 255;
        }

        return BBitmap.FromPixelsNoCopy(width, height, pixels);
    }

    // NormalizeForComparison used to run here, round-tripping each input through
    // `BBitmap.Decode(source.Encode(ImageEncodeFormat.Png, 100))` before any pixel was read.
    //
    // It was removed rather than made cheaper, because it was measured to be an identity: a
    // synthetic opaque image, one with graded alpha, one fully transparent with non-zero RGB (the
    // two cases where a PNG codec is entitled to premultiply or collapse the colour type), and 25
    // real WPT reference PNGs off disk all round-tripped to byte-identical pixels. It could not
    // have been anything else here — `Encode` serialises the same `_pixels` array `GetPixel` reads,
    // so a lossless round trip has nothing to normalise between two BBitmaps. Its own
    // `catch (InvalidOperationException) => source.Copy()` fallback already treated a plain copy as
    // an acceptable substitute.
    //
    // Cost of the no-op: ~141 ms per bitmap, ~282 ms of a 284 ms comparison, and 16-21% of a WPT
    // run. See tests/render-stages/results/pixel-compare.md in the parent repository, and
    // `--pixel-compare-cost`, which re-runs the identity check alongside the timings.
}
