using SkiaSharp;

namespace Broiler.HTML.Image.Adapters;

internal sealed class SkiaFontCompatFactory : IFontCompatFactory
{
    public static IFontCompatFactory Instance { get; } = new SkiaFontCompatFactory();

    public object CreateFont(object typeface, float size) =>
        new SKFont(SkiaCompatObjects.Typeface(typeface), size)
        {
            // Phase 10.2: Use grayscale anti-aliasing (Antialias) instead of
            // SubpixelAntialias. The Chromium reference screenshot is a bitmap
            // where sub-pixel colour fringes have been composited away, so
            // grayscale AA produces glyph shapes that match the reference more
            // closely and eliminates per-sub-pixel colour differences.
            // Priority 2: Enable sub-pixel text positioning (Subpixel = true)
            // for more precise glyph placement. This is orthogonal to the AA
            // edging mode and aligns baseline positioning with Chromium's
            // HarfBuzz/FreeType stack.
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
        };

    public FontCompatMetrics GetMetrics(object font)
    {
        var skFont = SkiaCompatObjects.Font(font);
        var skiaMetrics = skFont.Metrics;
        double height = skiaMetrics.Descent - skiaMetrics.Ascent;
        double underlineOffset = -skiaMetrics.Ascent
            + skiaMetrics.UnderlinePosition.GetValueOrDefault(skiaMetrics.Descent - skiaMetrics.Ascent * 0.87f);
        return new FontCompatMetrics(height, underlineOffset);
    }
}
