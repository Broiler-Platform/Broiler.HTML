using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Broiler.Graphics;
using BGraphicsFontStyle = Broiler.Graphics.FontStyle;

namespace Broiler.HTML.Image.Adapters.Text;

/// <summary>
/// Managed font store backing the real text backend.  Registered font files
/// are parsed into <see cref="TrueTypeFont"/> instances keyed by family/alias.
/// Families that were never registered resolve to <see cref="MissingTypeface"/>
/// so the shaper can fall back to the deterministic stub estimate (no glyphs),
/// preserving prior behaviour for unregistered/default fonts.
/// </summary>
internal sealed class TrueTypeTypefaceResolver : IFontTypefaceResolver
{
    private readonly object _sync = new();
    private readonly Dictionary<string, TrueTypeFont> _byFamily = new(StringComparer.OrdinalIgnoreCase);

    public string RegisterFontFile(string path, string alias = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var font = TrueTypeFont.LoadFromFile(path);
        if (font == null)
            return null;

        string family = !string.IsNullOrWhiteSpace(alias)
            ? alias
            : Path.GetFileNameWithoutExtension(path);

        if (string.IsNullOrWhiteSpace(family))
            return null;

        lock (_sync)
        {
            _byFamily[family] = font;
            if (!string.IsNullOrWhiteSpace(alias))
                _byFamily[alias] = font;
        }

        return family;
    }

    public bool HasDeferredLoadedTypefacePath(string family) => false;

    public bool HasMaterializedLoadedTypeface(string family)
    {
        if (string.IsNullOrWhiteSpace(family))
            return false;
        lock (_sync)
            return _byFamily.ContainsKey(family);
    }

    public object ResolveTypeface(string family, BGraphicsFontStyle style)
    {
        if (!string.IsNullOrWhiteSpace(family))
        {
            lock (_sync)
            {
                if (_byFamily.TryGetValue(family, out var font))
                    return font;
            }
        }

        return MissingTypeface.Instance;
    }
}

/// <summary>Sentinel returned when a requested font family is not registered.</summary>
internal sealed class MissingTypeface
{
    public static MissingTypeface Instance { get; } = new();
}

/// <summary>A typeface bound to a concrete size (carried through the font adapter).</summary>
internal sealed class TrueTypeScaledFont
{
    public TrueTypeScaledFont(TrueTypeFont font, float size)
    {
        Font = font;
        Size = size;
    }

    /// <summary>Parsed font, or <c>null</c> when the family was not registered.</summary>
    public TrueTypeFont Font { get; }

    /// <summary>The size this font instance was created with (points for the layout font).</summary>
    public float Size { get; }
}

/// <summary>
/// Font factory producing <see cref="TrueTypeScaledFont"/> handles.  Metrics
/// match the previous stub formula exactly so glyph rasterisation does not
/// shift the existing line layout.
/// </summary>
internal sealed class TrueTypeFontCompatFactory : IFontCompatFactory
{
    private const double PtToCssPx = 96.0 / 72.0;
    private const double LineHeightRatio = 1.16;
    private const double UnderlineRatio = 0.85;

    public static TrueTypeFontCompatFactory Instance { get; } = new();

    public object CreateFont(object typeface, float size) =>
        new TrueTypeScaledFont(typeface as TrueTypeFont, size);

    public FontCompatMetrics GetMetrics(object font)
    {
        double sizePt = font is TrueTypeScaledFont f ? f.Size : 0.0;
        double heightPx = sizePt * PtToCssPx * LineHeightRatio;
        return new FontCompatMetrics(heightPx, heightPx * UnderlineRatio);
    }
}

/// <summary>
/// Text shaper that rasterises real glyph outlines for registered fonts and
/// falls back to the deterministic stub estimate (sensible metrics, no glyphs)
/// for unregistered/default families.
/// </summary>
internal sealed class TrueTypeTextShaper : ITextShaper
{
    private const double PtToCssPx = 96.0 / 72.0;
    private const double AverageGlyphWidthRatio = 0.5;

    public static TrueTypeTextShaper Instance { get; } = new();

    public SizeF MeasureString(FontAdapter font, string text)
    {
        float height = (float)font.Height;
        if (string.IsNullOrEmpty(text))
            return new SizeF(0f, height);

        if (TryGetFont(font, out var ttf, out float scale))
            return new SizeF(MeasureAdvances(ttf, text, scale), height);

        return new SizeF(text.Length * StubGlyphWidth(font), height);
    }

    public void MeasureString(FontAdapter font, string text, double maxWidth, out int charFit, out double charFitWidth)
    {
        int length = text?.Length ?? 0;
        if (length == 0)
        {
            charFit = 0;
            charFitWidth = 0;
            return;
        }

        if (TryGetFont(font, out var ttf, out float scale))
        {
            double width = 0;
            int fit = 0;
            for (int i = 0; i < length; i++)
            {
                int cp = char.ConvertToUtf32(text, i);
                if (char.IsHighSurrogate(text[i]) && i + 1 < length)
                    i++;

                double advance = ttf.GetAdvanceWidth(ttf.GetGlyphIndex(cp)) * scale;
                if (width + advance > maxWidth)
                    break;
                width += advance;
                fit++;
            }

            charFit = fit;
            charFitWidth = width;
            return;
        }

        float glyphWidth = StubGlyphWidth(font);
        if (glyphWidth <= 0f)
        {
            charFit = length;
            charFitWidth = 0;
            return;
        }

        int stubFit = Math.Clamp((int)Math.Floor(maxWidth / glyphWidth), 0, length);
        charFit = stubFit;
        charFitWidth = stubFit * glyphWidth;
    }

    public bool TryDrawString(BCanvas canvas, FontAdapter font, string text, Color color, PointF point)
    {
        if (!string.IsNullOrEmpty(text) && TryGetFont(font, out var ttf, out float scale))
            DrawGlyphs(canvas, ttf, scale, text, new BColor(color.R, color.G, color.B, color.A), point);

        // Returning true reports the text as handled (glyphs drawn, or
        // intentionally skipped for an unregistered font) so the raster path
        // does not fall back to a non-existent canvas.
        return true;
    }

    public bool TryDrawGradientString(BCanvas canvas, FontAdapter font, string text, RectangleF rect, PointF point, SizeF size, Color[] colors, float[] positions, float angle)
    {
        // Gradient-filled text is not yet rasterised; skip cleanly (no glyphs),
        // matching prior behaviour, rather than drawing a wrong solid colour.
        return true;
    }

    public void DrawString(object canvas, FontAdapter font, string text, Color color, PointF point)
    {
        // Only the raster (BCanvas) path is supported; nothing to do here.
    }

    public void DrawGradientString(object canvas, FontAdapter font, string text, RectangleF rect, PointF point, SizeF size, Color[] colors, float[] positions, float angle)
    {
    }

    private static void DrawGlyphs(BCanvas canvas, TrueTypeFont ttf, float scale, string text, BColor color, PointF point)
    {
        float penX = point.X;
        float baselineY = point.Y + ttf.Ascender * scale;

        for (int i = 0; i < text.Length; i++)
        {
            int cp = char.ConvertToUtf32(text, i);
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length)
                i++;

            int glyph = ttf.GetGlyphIndex(cp);
            var fontContours = ttf.GetGlyphContours(glyph);
            if (fontContours.Count > 0)
            {
                var userContours = new List<PointF[]>(fontContours.Count);
                foreach (var contour in fontContours)
                {
                    var transformed = new PointF[contour.Length];
                    for (int j = 0; j < contour.Length; j++)
                    {
                        // Font units are y-up; flip to the canvas's y-down space.
                        transformed[j] = new PointF(
                            penX + contour[j].X * scale,
                            baselineY - contour[j].Y * scale);
                    }
                    userContours.Add(transformed);
                }

                canvas.FillGlyphContours(userContours, color);
            }

            penX += ttf.GetAdvanceWidth(glyph) * scale;
        }
    }

    private static float MeasureAdvances(TrueTypeFont ttf, string text, float scale)
    {
        float width = 0f;
        for (int i = 0; i < text.Length; i++)
        {
            int cp = char.ConvertToUtf32(text, i);
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length)
                i++;

            width += ttf.GetAdvanceWidth(ttf.GetGlyphIndex(cp)) * scale;
        }
        return width;
    }

    private static bool TryGetFont(FontAdapter font, out TrueTypeFont ttf, out float scale)
    {
        if (font.Typeface is TrueTypeFont parsed && parsed.HasOutlines)
        {
            ttf = parsed;
            float pxSize = (float)(font.Size * PtToCssPx);
            scale = pxSize / parsed.UnitsPerEm;
            return true;
        }

        ttf = null;
        scale = 0f;
        return false;
    }

    private static float StubGlyphWidth(FontAdapter font) =>
        (float)(font.Size * PtToCssPx * AverageGlyphWidthRatio);
}
