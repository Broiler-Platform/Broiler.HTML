using Broiler.HTML.Image.Adapters;
using System;
using System.Drawing;
using System.IO;
using Broiler.Graphics;

// OS-free stand-ins for the former GDI+ compatibility leaves. The default
// "broiler" raster pipeline draws shapes, solid/gradient fills and images
// directly, so these stubs are only reached for operations that previously
// depended on GDI+. They degrade gracefully (skip the visual) instead of
// throwing, except where a result genuinely cannot be produced.

namespace Broiler.HTML.Image.Compat;

/// <summary>
/// Text metrics and drawing stub. Measurement returns a deterministic estimate
/// so layout stays sensible; glyph drawing is skipped until a real text backend
/// is registered.
/// </summary>
internal sealed class StubTextShaper : ITextShaper
{
    private const double PtToCssPx = 96.0 / 72.0;
    private const double AverageGlyphWidthRatio = 0.5;

    public SizeF MeasureString(FontAdapter font, string text)
    {
        float height = (float)font.Height;
        if (string.IsNullOrEmpty(text))
            return new SizeF(0f, height);

        return new SizeF(text.Length * GlyphWidth(font), height);
    }

    public void MeasureString(FontAdapter font, string text, double maxWidth, out int charFit, out double charFitWidth)
    {
        int length = text?.Length ?? 0;
        float glyphWidth = GlyphWidth(font);
        if (length == 0 || glyphWidth <= 0f)
        {
            charFit = length;
            charFitWidth = 0;
            return;
        }

        int fit = (int)Math.Floor(maxWidth / glyphWidth);
        fit = Math.Clamp(fit, 0, length);
        charFit = fit;
        charFitWidth = fit * glyphWidth;
    }

    // Returning true tells the raster path the text was handled, so it is skipped
    // cleanly instead of falling back to the (removed) GDI canvas.
    public bool TryDrawString(BCanvas canvas, FontAdapter font, string text, BColor color, PointF point, float glyphRotationDeg = 0f) => true;

    public bool TryDrawGradientString(BCanvas canvas, FontAdapter font, string text, RectangleF rect, PointF point, SizeF size, BColor[] colors, float[] positions, float angle) => true;

    public void DrawString(object canvas, FontAdapter font, string text, BColor color, PointF point)
    {
        // No text backend: glyphs are not rendered.
    }

    public void DrawGradientString(object canvas, FontAdapter font, string text, RectangleF rect, PointF point, SizeF size, BColor[] colors, float[] positions, float angle)
    {
        // No text backend: glyphs are not rendered.
    }

    private static float GlyphWidth(FontAdapter font) => (float)(font.Size * PtToCssPx * AverageGlyphWidthRatio);
}

/// <summary>Canvas-fallback stub; every operation is a no-op.</summary>
internal sealed class StubCanvasCompat : ICanvasCompat
{
    public static StubCanvasCompat Instance { get; } = new();

    public void PushClip(object canvas, RectangleF rect) { }

    public void PushClipExclude(object canvas, RectangleF rect) { }

    public void DrawLine(object canvas, float x1, float y1, float x2, float y2, object paint) { }

    public void DrawRectangle(object canvas, RectangleF rect, object paint) { }

    public void DrawImage(object canvas, BBitmap bitmap, RectangleF destRect, RectangleF srcRect) { }

    public void DrawImage(object canvas, BBitmap bitmap, RectangleF destRect) { }

    public void DrawPath(object canvas, GraphicsPathAdapter path, object paint) { }

    public void ClipRounded(
        object canvas,
        RectangleF rect,
        double cornerNw,
        double cornerNwY,
        double cornerNe,
        double cornerNeY,
        double cornerSe,
        double cornerSeY,
        double cornerSw,
        double cornerSwY)
    { }

    public object CreateTexturePaint(BBitmap bitmap, PointF translateTransformLocation) => StubPaint.Instance;

    public void DrawPolygon(object canvas, PointF[] points, object paint) { }

    public void SaveOpacityLayer(object canvas, float opacity) { }

    public void SaveBlendLayer(object canvas, string blendMode) { }

    public void SaveTransformLayer(object canvas, float[] matrix, float originX, float originY) { }
}

/// <summary>Path-builder stub; produces an inert path object.</summary>
internal sealed class StubPathCompat : IPathCompat
{
    public static StubPathCompat Instance { get; } = new();

    public object CreatePath() => new object();

    public void Reset(object path) { }

    public void MoveTo(object path, float x, float y) { }

    public void LineTo(object path, float x, float y) { }

    public void ArcTo(object path, float left, float top, float width, float height, float startAngle, float sweepAngle, bool forceMoveTo) { }
}

/// <summary>Paint-factory stub; produces inert paint sentinels.</summary>
internal sealed class StubPaintCompatFactory : IPaintCompatFactory
{
    public static StubPaintCompatFactory Instance { get; } = new();

    public object CreateSolidBrushPaint(BColor color) => StubPaint.Instance;

    public object CreateLinearGradientBrushPaint(RectangleF rect, BColor color1, BColor color2, double angle) => StubPaint.Instance;

    public object CreatePenPaint(BColor color, float strokeWidth, Graphics.DashStyle dashStyle) => StubPaint.Instance;

    public void UpdatePenPaint(object paint, float strokeWidth, Graphics.DashStyle dashStyle) { }
}

/// <summary>Font-factory stub; carries the requested size so metrics stay sensible.</summary>
internal sealed class StubFontCompatFactory : IFontCompatFactory
{
    private const double PtToCssPx = 96.0 / 72.0;
    private const double LineHeightRatio = 1.16;
    private const double UnderlineRatio = 0.85;

    public static StubFontCompatFactory Instance { get; } = new();

    public object CreateFont(object typeface, float size) => new StubFont(size);

    public FontCompatMetrics GetMetrics(object font)
    {
        double sizePt = font is StubFont f ? f.SizePt : 0.0;
        double heightPx = sizePt * PtToCssPx * LineHeightRatio;
        return new FontCompatMetrics(heightPx, heightPx * UnderlineRatio);
    }
}

/// <summary>Typeface-resolver stub; records family names but resolves to a sentinel.</summary>
internal sealed class StubFontTypefaceResolver : IFontTypefaceResolver
{
    public string RegisterFontFile(string path, string alias = null)
    {
        if (!string.IsNullOrWhiteSpace(alias))
            return alias;

        return string.IsNullOrWhiteSpace(path) ? null : Path.GetFileNameWithoutExtension(path);
    }

    public bool HasDeferredLoadedTypefacePath(string family) => false;

    public bool HasMaterializedLoadedTypeface(string family) => false;

    public object ResolveTypeface(string family, Graphics.FontStyle style) => StubTypeface.Instance;
}

/// <summary>Inert layout font carrying the size used for stub metrics.</summary>
internal sealed class StubFont(float sizePt)
{
    public float SizePt { get; } = sizePt;
}

/// <summary>Shared inert paint sentinel.</summary>
internal sealed class StubPaint
{
    public static StubPaint Instance { get; } = new();
}

/// <summary>Shared inert typeface sentinel.</summary>
internal sealed class StubTypeface
{
    public static StubTypeface Instance { get; } = new();
}
