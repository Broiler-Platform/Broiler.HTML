using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Broiler.HTML.Image.Adapters;

internal sealed class GdiCanvasCompat : ICanvasCompat
{
    public static ICanvasCompat Instance { get; } = new GdiCanvasCompat();

    public void PushClip(object canvas, RectangleF rect) =>
        GdiCompatObjects.Canvas(canvas).Graphics.SetClip(rect, CombineMode.Intersect);

    public void PushClipExclude(object canvas, RectangleF rect) =>
        GdiCompatObjects.Canvas(canvas).Graphics.SetClip(rect, CombineMode.Exclude);

    public void DrawLine(object canvas, float x1, float y1, float x2, float y2, object paint) =>
        GdiCompatObjects.Canvas(canvas).Graphics.DrawLine(GdiCompatObjects.Paint(paint).Pen!, x1, y1, x2, y2);

    public void DrawRectangle(object canvas, RectangleF rect, object paint)
    {
        var graphics = GdiCompatObjects.Canvas(canvas).Graphics;
        var gdiPaint = GdiCompatObjects.Paint(paint);
        if (gdiPaint.IsPen)
            graphics.DrawRectangle(gdiPaint.Pen!, rect.X, rect.Y, rect.Width, rect.Height);
        else
            graphics.FillRectangle(gdiPaint.Brush!, rect);
    }

    public void DrawImage(object canvas, BBitmap bitmap, RectangleF destRect, RectangleF srcRect) =>
        GdiCompatObjects.Canvas(canvas).Graphics.DrawImage(
            GdiCompatObjects.Bitmap(bitmap.AsCompatBitmap()),
            destRect,
            srcRect,
            GraphicsUnit.Pixel);

    public void DrawImage(object canvas, BBitmap bitmap, RectangleF destRect) =>
        GdiCompatObjects.Canvas(canvas).Graphics.DrawImage(GdiCompatObjects.Bitmap(bitmap.AsCompatBitmap()), destRect);

    public void DrawPath(object canvas, GraphicsPathAdapter path, object paint)
    {
        var graphics = GdiCompatObjects.Canvas(canvas).Graphics;
        var gdiPaint = GdiCompatObjects.Paint(paint);
        var gdiPath = GdiCompatObjects.Path(path.Path).Path;
        if (gdiPaint.IsPen)
            graphics.DrawPath(gdiPaint.Pen!, gdiPath);
        else
            graphics.FillPath(gdiPaint.Brush!, gdiPath);
    }

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
    {
        var graphics = GdiCompatObjects.Canvas(canvas).Graphics;
        if (cornerNw <= 0 && cornerNwY <= 0
            && cornerNe <= 0 && cornerNeY <= 0
            && cornerSe <= 0 && cornerSeY <= 0
            && cornerSw <= 0 && cornerSwY <= 0)
        {
            graphics.SetClip(rect, CombineMode.Intersect);
            return;
        }

        using var path = CreateRoundedRectPath(
            rect,
            (float)cornerNw, (float)cornerNwY,
            (float)cornerNe, (float)cornerNeY,
            (float)cornerSe, (float)cornerSeY,
            (float)cornerSw, (float)cornerSwY);
        graphics.SetClip(path, CombineMode.Intersect);
    }

    public object CreateTexturePaint(BBitmap bitmap, PointF translateTransformLocation)
    {
        var brush = new TextureBrush(GdiCompatObjects.Bitmap(bitmap.AsCompatBitmap()))
        {
            WrapMode = WrapMode.Tile,
        };
        brush.TranslateTransform(translateTransformLocation.X, translateTransformLocation.Y);
        return new GdiPaint { Brush = brush };
    }

    public void DrawPolygon(object canvas, PointF[] points, object paint)
    {
        var graphics = GdiCompatObjects.Canvas(canvas).Graphics;
        var gdiPaint = GdiCompatObjects.Paint(paint);
        if (gdiPaint.IsPen)
            graphics.DrawPolygon(gdiPaint.Pen!, points);
        else
            graphics.FillPolygon(gdiPaint.Brush!, points);
    }

    public void SaveOpacityLayer(object canvas, float opacity)
    {
        // GDI+ has no compositing layer with a blend alpha; the Broiler raster
        // pipeline handles real opacity layers. On this fallback path we balance
        // the save/restore so transforms and clips stay consistent.
        GdiCompatObjects.Canvas(canvas).Save();
    }

    public void SaveBlendLayer(object canvas, string blendMode)
    {
        // See SaveOpacityLayer: blend modes are handled by the raster pipeline.
        GdiCompatObjects.Canvas(canvas).Save();
    }

    public void SaveTransformLayer(object canvas, float[] matrix, float originX, float originY)
    {
        var gdiCanvas = GdiCompatObjects.Canvas(canvas);
        gdiCanvas.Save();
        var graphics = gdiCanvas.Graphics;
        graphics.TranslateTransform(originX, originY);
        using var transform = new Matrix(matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5]);
        graphics.MultiplyTransform(transform);
        graphics.TranslateTransform(-originX, -originY);
    }

    private static GraphicsPath CreateRoundedRectPath(
        RectangleF rect,
        float cornerNw, float cornerNwY,
        float cornerNe, float cornerNeY,
        float cornerSe, float cornerSeY,
        float cornerSw, float cornerSwY)
    {
        float left = rect.Left;
        float top = rect.Top;
        float right = rect.Right;
        float bottom = rect.Bottom;

        var path = new GraphicsPath();

        // Top edge and top-right corner.
        path.AddLine(left + cornerNw, top, right - cornerNe, top);
        if (cornerNe > 0 && cornerNeY > 0)
            path.AddArc(right - (cornerNe * 2), top, cornerNe * 2, cornerNeY * 2, 270, 90);

        // Right edge and bottom-right corner.
        path.AddLine(right, top + cornerNeY, right, bottom - cornerSeY);
        if (cornerSe > 0 && cornerSeY > 0)
            path.AddArc(right - (cornerSe * 2), bottom - (cornerSeY * 2), cornerSe * 2, cornerSeY * 2, 0, 90);

        // Bottom edge and bottom-left corner.
        path.AddLine(right - cornerSe, bottom, left + cornerSw, bottom);
        if (cornerSw > 0 && cornerSwY > 0)
            path.AddArc(left, bottom - (cornerSwY * 2), cornerSw * 2, cornerSwY * 2, 90, 90);

        // Left edge and top-left corner.
        path.AddLine(left, bottom - cornerSwY, left, top + cornerNwY);
        if (cornerNw > 0 && cornerNwY > 0)
            path.AddArc(left, top, cornerNw * 2, cornerNwY * 2, 180, 90);

        path.CloseFigure();
        return path;
    }
}
