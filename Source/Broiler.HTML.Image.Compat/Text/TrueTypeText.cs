using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Broiler.Graphics;
using Broiler.HTML.Image.Adapters;
using BGraphicsFontStyle = Broiler.Graphics.FontStyle;

namespace Broiler.HTML.Image.Compat.Text;

/// <summary>
/// Managed font store backing the real text backend.  Registered font files
/// are parsed into <see cref="TrueTypeFont"/> instances keyed by family/alias.
/// Families that were never registered resolve to <see cref="MissingTypeface"/>
/// so the shaper can fall back to the deterministic stub estimate (no glyphs),
/// preserving prior behaviour for unregistered/default fonts.
/// </summary>
internal sealed class TrueTypeTypefaceResolver : IFontTypefaceResolver
{
    private const string FallbackResourceName =
        "Broiler.HTML.Image.Compat.Fonts.Vazirmatn-Regular.ttf";

    private readonly object _sync = new();
    private readonly Dictionary<string, TrueTypeFont> _byFamily = new(StringComparer.OrdinalIgnoreCase);

    private TrueTypeFont _fallback;
    private bool _fallbackLoaded;

    /// <summary>
    /// Bundled OFL fallback font (Vazirmatn) used for unregistered/default CSS
    /// families.  Loaded lazily from the embedded resource; <c>null</c> if the
    /// resource is missing or fails to parse.
    /// </summary>
    private TrueTypeFont GetFallbackFont()
    {
        lock (_sync)
        {
            if (_fallbackLoaded)
                return _fallback;
            _fallbackLoaded = true;

            try
            {
                var asm = typeof(TrueTypeTypefaceResolver).Assembly;
                using var stream = asm.GetManifestResourceStream(FallbackResourceName);
                if (stream != null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    _fallback = TrueTypeFont.Load(ms.ToArray());
                }
            }
            catch
            {
                _fallback = null;
            }

            return _fallback;
        }
    }

    public string RegisterFontFile(string path, string alias = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var font = TrueTypeFont.LoadFromFile(path);
        if (font == null)
            return null;

        // Reject fonts we cannot rasterise (e.g. CFF/PostScript-outline OpenType,
        // which has no glyf table) so the family falls back to the bundled font
        // and still renders glyphs instead of nothing.
        if (!font.HasOutlines)
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

        // Unregistered/default family: fall back to the bundled font so that
        // text still renders glyphs (instead of nothing).  Returns
        // MissingTypeface only if the bundled font is unavailable.
        return (object)GetFallbackFont() ?? MissingTypeface.Instance;
    }
}

/// <summary>Sentinel returned when a requested font family is not registered.</summary>
internal sealed class MissingTypeface
{
    public static MissingTypeface Instance { get; } = new();
}

/// <summary>A typeface bound to a concrete size (carried through the font adapter).</summary>
internal sealed class TrueTypeScaledFont(float size)
{
    /// <summary>The size this font instance was created with (points for the layout font).</summary>
    public float Size { get; } = size;
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
        new TrueTypeScaledFont(size);

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
            return new SizeF(MeasureAdvances(ttf, text, scale, font.FontFeatures), height);

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
            for (int i = 0; i < length;)
            {
                int cp = UnicodeCodepointReader.ReadCodePoint(text, i, out int nextIndex);

                double advance = ttf.GetAdvanceWidth(ttf.GetGlyphIndex(cp)) * scale;
                if (width + advance > maxWidth)
                    break;
                width += advance;
                fit = nextIndex;
                i = nextIndex;
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

    public bool TryDrawString(BCanvas canvas, FontAdapter font, string text, BColor color, PointF point, float glyphRotationDeg = 0f)
    {
        if (!string.IsNullOrEmpty(text) && TryGetFont(font, out var ttf, out float scale))
            DrawGlyphs(canvas, ttf, scale, text, new BColor(color.R, color.G, color.B, color.A), point, font.FontFeatures, glyphRotationDeg);

        // Returning true reports the text as handled (glyphs drawn, or
        // intentionally skipped for an unregistered font) so the raster path
        // does not fall back to a non-existent canvas.
        return true;
    }

    public bool TryDrawGradientString(BCanvas canvas, FontAdapter font, string text, RectangleF rect, PointF point, SizeF size, BColor[] colors, float[] positions, float angle)
    {
        // Gradient-filled text is not yet rasterised; skip cleanly (no glyphs),
        // matching prior behaviour, rather than drawing a wrong solid colour.
        return true;
    }

    public void DrawString(object canvas, FontAdapter font, string text, BColor color, PointF point)
    {
        // Only the raster (BCanvas) path is supported; nothing to do here.
    }

    public void DrawGradientString(object canvas, FontAdapter font, string text, RectangleF rect, PointF point, SizeF size, BColor[] colors, float[] positions, float angle)
    {
    }

    private static void DrawGlyphs(BCanvas canvas, TrueTypeFont ttf, float scale, string text, BColor color, PointF point, string features = null, float glyphRotationDeg = 0f)
    {
        float penX = point.X;
        float baselineY = point.Y + ttf.Ascender * scale;

        // PROTOTYPE Stage 2: text-orientation:mixed rotates each glyph 90°
        // clockwise about the centre of its em box.  The layout transform has
        // already stacked the per-glyph cells down the column, so rotating each
        // glyph in place yields sideways vertical text.
        bool rotate = glyphRotationDeg != 0f;
        float emHalf = (ttf.Ascender - ttf.Descender) * scale * 0.5f;
        float pivotX = point.X + emHalf;
        float pivotY = point.Y + emHalf;

        // Complex scripts (Arabic/Persian), right-to-left text, or requested
        // OpenType features (font-feature-settings) need GSUB shaping before
        // they can be drawn glyph-by-glyph.
        if (ComplexTextShaper.RequiresShaping(text, features))
        {
            foreach (var shaped in ComplexTextShaper.Shape(ttf, text, features))
            {
                // GPOS offsets shift the glyph relative to the pen without
                // changing the advance (font units are y-up, canvas is y-down).
                DrawGlyphOutline(canvas, ttf, shaped.Glyph, scale,
                    penX + shaped.XOffset * scale,
                    baselineY - shaped.YOffset * scale,
                    color, glyphRotationDeg, pivotX, pivotY);
                penX += shaped.Advance * scale;
            }
            return;
        }

        for (int i = 0; i < text.Length;)
        {
            int cp = UnicodeCodepointReader.ReadCodePoint(text, i, out int nextIndex);

            int glyph = ttf.GetGlyphIndex(cp);
            DrawGlyphOutline(canvas, ttf, glyph, scale, penX, baselineY, color,
                rotate ? glyphRotationDeg : 0f, pivotX, pivotY);
            penX += ttf.GetAdvanceWidth(glyph) * scale;
            i = nextIndex;
        }
    }

    private static void DrawGlyphOutline(BCanvas canvas, TrueTypeFont ttf, int glyph, float scale, float penX, float baselineY, BColor color, float glyphRotationDeg = 0f, float pivotX = 0f, float pivotY = 0f)
    {
        var fontContours = ttf.GetGlyphContours(glyph);
        if (fontContours.Count == 0)
            return;

        // Clockwise rotation in the canvas's y-down space: [[cos,-sin],[sin,cos]].
        bool rotate = glyphRotationDeg != 0f;
        float cos = 0f, sin = 0f;
        if (rotate)
        {
            double rad = glyphRotationDeg * Math.PI / 180.0;
            cos = (float)Math.Cos(rad);
            sin = (float)Math.Sin(rad);
        }

        var userContours = new List<PointF[]>(fontContours.Count);
        foreach (var contour in fontContours)
        {
            var transformed = new PointF[contour.Length];
            for (int j = 0; j < contour.Length; j++)
            {
                // Font units are y-up; flip to the canvas's y-down space.
                float x = penX + contour[j].X * scale;
                float y = baselineY - contour[j].Y * scale;

                if (rotate)
                {
                    float dx = x - pivotX;
                    float dy = y - pivotY;
                    x = pivotX + (cos * dx - sin * dy);
                    y = pivotY + (sin * dx + cos * dy);
                }

                transformed[j] = new PointF(x, y);
            }
            userContours.Add(transformed);
        }

        canvas.FillGlyphContours(userContours, color);
    }

    private static float MeasureAdvances(TrueTypeFont ttf, string text, float scale, string features = null)
    {
        float width = 0f;

        if (ComplexTextShaper.RequiresShaping(text, features))
        {
            foreach (var shaped in ComplexTextShaper.Shape(ttf, text, features))
                width += shaped.Advance * scale;
            return width;
        }

        for (int i = 0; i < text.Length;)
        {
            int cp = UnicodeCodepointReader.ReadCodePoint(text, i, out int nextIndex);

            width += ttf.GetAdvanceWidth(ttf.GetGlyphIndex(cp)) * scale;
            i = nextIndex;
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
