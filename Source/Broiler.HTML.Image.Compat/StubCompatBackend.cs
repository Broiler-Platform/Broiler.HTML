using Broiler.HTML.Image.Adapters;
using System.Drawing;
using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.Graphics.Rendering;

// OS-free stand-ins for the former GDI+ compatibility leaves. The default
// "broiler" raster pipeline draws shapes, solid/gradient fills and images
// directly, so these stubs are only reached for operations that previously
// depended on GDI+. They degrade gracefully (skip the visual) instead of
// throwing, except where a result genuinely cannot be produced.

namespace Broiler.HTML.Image.Compat;

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

    public object CreatePenPaint(BColor color, float strokeWidth, DashStyle dashStyle) => StubPaint.Instance;

    public void UpdatePenPaint(object paint, float strokeWidth, DashStyle dashStyle) { }
}

/// <summary>Shared inert paint sentinel.</summary>
internal sealed class StubPaint
{
    public static StubPaint Instance { get; } = new();
}
