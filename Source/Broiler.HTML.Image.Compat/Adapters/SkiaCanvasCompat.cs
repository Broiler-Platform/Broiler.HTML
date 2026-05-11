using System;
using System.Drawing;
using SkiaSharp;

namespace Broiler.HTML.Image.Adapters;

internal sealed class SkiaCanvasCompat : ICanvasCompat
{
    public static ICanvasCompat Instance { get; } = new SkiaCanvasCompat();

    public void PushClip(object canvas, RectangleF rect) =>
        SkiaCompatObjects.Canvas(canvas).ClipRect(Utilities.Utils.Convert(rect));

    public void PushClipExclude(object canvas, RectangleF rect) =>
        SkiaCompatObjects.Canvas(canvas).ClipRect(Utilities.Utils.Convert(rect), SKClipOperation.Difference);

    public void DrawLine(object canvas, float x1, float y1, float x2, float y2, object paint) =>
        SkiaCompatObjects.Canvas(canvas).DrawLine(x1, y1, x2, y2, SkiaCompatObjects.Paint(paint));

    public void DrawRectangle(object canvas, RectangleF rect, object paint) =>
        SkiaCompatObjects.Canvas(canvas).DrawRect(Utilities.Utils.Convert(rect), SkiaCompatObjects.Paint(paint));

    public void DrawImage(object canvas, BBitmap bitmap, RectangleF destRect, RectangleF srcRect) =>
        SkiaCompatObjects.Canvas(canvas).DrawBitmap(
            SkiaCompatObjects.Bitmap(bitmap.AsCompatBitmap()),
            Utilities.Utils.Convert(srcRect),
            Utilities.Utils.Convert(destRect));

    public void DrawImage(object canvas, BBitmap bitmap, RectangleF destRect) =>
        SkiaCompatObjects.Canvas(canvas).DrawBitmap(SkiaCompatObjects.Bitmap(bitmap.AsCompatBitmap()), Utilities.Utils.Convert(destRect));

    public void DrawPath(object canvas, GraphicsPathAdapter path, object paint) =>
        SkiaCompatObjects.Canvas(canvas).DrawPath(SkiaCompatObjects.Path(path.Path), SkiaCompatObjects.Paint(paint));

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
        var skCanvas = SkiaCompatObjects.Canvas(canvas);
        if ((cornerNw <= 0 && cornerNwY <= 0)
            && (cornerNe <= 0 && cornerNeY <= 0)
            && (cornerSe <= 0 && cornerSeY <= 0)
            && (cornerSw <= 0 && cornerSwY <= 0))
        {
            skCanvas.ClipRect(Utilities.Utils.Convert(rect));
            return;
        }

        var skRect = Utilities.Utils.Convert(rect);
        var radii = new[]
        {
            new SKPoint((float)cornerNw, (float)cornerNwY),
            new SKPoint((float)cornerNe, (float)cornerNeY),
            new SKPoint((float)cornerSe, (float)cornerSeY),
            new SKPoint((float)cornerSw, (float)cornerSwY),
        };
        var rrect = new SKRoundRect();
        rrect.SetRectRadii(skRect, radii);
        skCanvas.ClipRoundRect(rrect);
    }

    public object CreateTexturePaint(BBitmap bitmap, PointF translateTransformLocation)
    {
        var paint = new SKPaint();
        paint.Shader = SKShader.CreateBitmap(
            SkiaCompatObjects.Bitmap(bitmap.AsCompatBitmap()),
            SKShaderTileMode.Repeat,
            SKShaderTileMode.Repeat,
            SKMatrix.CreateTranslation((float)translateTransformLocation.X, (float)translateTransformLocation.Y));
        return paint;
    }

    public void DrawPolygon(object canvas, PointF[] points, object paint)
    {
        using var path = new SKPath();
        path.MoveTo(Utilities.Utils.Convert(points[0]));

        for (int i = 1; i < points.Length; i++)
            path.LineTo(Utilities.Utils.Convert(points[i]));

        path.Close();
        SkiaCompatObjects.Canvas(canvas).DrawPath(path, SkiaCompatObjects.Paint(paint));
    }

    public void SaveOpacityLayer(object canvas, float opacity)
    {
        byte alpha = (byte)Math.Clamp((int)(opacity * 255), 0, 255);
        using var paint = new SKPaint { Color = new SKColor(0, 0, 0, alpha) };
        SkiaCompatObjects.Canvas(canvas).SaveLayer(paint);
    }

    public void SaveBlendLayer(object canvas, string blendMode)
    {
        var skBlendMode = blendMode?.ToLowerInvariant() switch
        {
            "multiply" => SKBlendMode.Multiply,
            "screen" => SKBlendMode.Screen,
            "overlay" => SKBlendMode.Overlay,
            "darken" => SKBlendMode.Darken,
            "lighten" => SKBlendMode.Lighten,
            "color-dodge" => SKBlendMode.ColorDodge,
            "color-burn" => SKBlendMode.ColorBurn,
            "hard-light" => SKBlendMode.HardLight,
            "soft-light" => SKBlendMode.SoftLight,
            "difference" => SKBlendMode.Difference,
            "exclusion" => SKBlendMode.Exclusion,
            "hue" => SKBlendMode.Hue,
            "saturation" => SKBlendMode.Saturation,
            "color" => SKBlendMode.Color,
            "luminosity" => SKBlendMode.Luminosity,
            "plus-lighter" => SKBlendMode.Plus,
            _ => SKBlendMode.SrcOver,
        };

        using var paint = new SKPaint { BlendMode = skBlendMode };
        SkiaCompatObjects.Canvas(canvas).SaveLayer(paint);
    }
}
