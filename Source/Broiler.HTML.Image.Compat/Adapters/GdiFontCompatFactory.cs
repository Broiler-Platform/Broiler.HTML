using System;
using System.Drawing;

namespace Broiler.HTML.Image.Adapters;

internal sealed class GdiFontCompatFactory : IFontCompatFactory
{
    public static IFontCompatFactory Instance { get; } = new GdiFontCompatFactory();

    public object CreateFont(object typeface, float size)
    {
        var gdiTypeface = GdiCompatObjects.Typeface(typeface);
        float emSize = size <= 0 ? 1f : size;
        var style = ResolveAvailableStyle(gdiTypeface.Family, gdiTypeface.Style);
        return new Font(gdiTypeface.Family, emSize, style, GraphicsUnit.Pixel);
    }

    public FontCompatMetrics GetMetrics(object font)
    {
        var gdiFont = GdiCompatObjects.Font(font);
        var family = gdiFont.FontFamily;
        var style = gdiFont.Style;

        float emHeight = family.GetEmHeight(style);
        if (emHeight <= 0)
            emHeight = 1;

        float ascent = family.GetCellAscent(style);
        float descent = family.GetCellDescent(style);
        double scale = gdiFont.Size / emHeight;

        double height = (ascent + descent) * scale;
        // Place the underline just below the baseline (the baseline sits at the
        // ascent measured from the top of the line box).
        double underlineOffset = (ascent + (descent * 0.5)) * scale;
        return new FontCompatMetrics(height, underlineOffset);
    }

    private static FontStyle ResolveAvailableStyle(FontFamily family, FontStyle requested)
    {
        if (family.IsStyleAvailable(requested))
            return requested;

        foreach (var candidate in new[] { FontStyle.Regular, FontStyle.Bold, FontStyle.Italic, FontStyle.Bold | FontStyle.Italic })
        {
            if (family.IsStyleAvailable(candidate))
                return candidate;
        }

        return FontStyle.Regular;
    }
}
