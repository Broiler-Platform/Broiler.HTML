using System;
using System.Drawing;
using SkiaSharp;

namespace Broiler.HTML.Image.Adapters;

internal sealed class SkiaTextCanvasCompat : ITextCanvasCompat
{
    public static ITextCanvasCompat Instance { get; } = new SkiaTextCanvasCompat();

    public void DrawString(object canvas, FontAdapter font, object renderFont, string text, Color color, PointF point)
    {
        var skCanvas = SkiaCompatObjects.Canvas(canvas);
        var skRenderFont = SkiaCompatObjects.Font(renderFont);
        var origin = GetDrawOrigin(skRenderFont, point);
        using var paint = new SKPaint
        {
            Color = Utilities.Utils.Convert(color),
            IsAntialias = true,
        };

        skCanvas.DrawText(text, origin.X, origin.Y, skRenderFont, paint);
    }

    public void DrawGradientString(
        object canvas,
        FontAdapter font,
        object renderFont,
        string text,
        RectangleF rect,
        PointF point,
        SizeF size,
        Color[] colors,
        float[] positions,
        float angle)
    {
        var skCanvas = SkiaCompatObjects.Canvas(canvas);
        var skRenderFont = SkiaCompatObjects.Font(renderFont);
        var origin = GetDrawOrigin(skRenderFont, point);
        float shaderWidth = Math.Max(rect.Width, skRenderFont.MeasureText(text));
        float shaderHeight = Math.Max(rect.Height > 0 ? rect.Height : size.Height, (float)font.Size);
        var shaderRect = new RectangleF(rect.X, rect.Y, shaderWidth, shaderHeight);

        var (startPoint, endPoint) = GetGradientEndpoints(shaderRect, angle);
        var skColors = new SKColor[colors.Length];
        for (int i = 0; i < colors.Length; i++)
            skColors[i] = Utilities.Utils.Convert(colors[i]);

        skCanvas.SaveLayer();
        using (var maskPaint = new SKPaint { Color = SKColors.White, IsAntialias = false })
        {
            skCanvas.DrawText(text, origin.X, origin.Y, skRenderFont, maskPaint);
        }

        using var shader = SKShader.CreateLinearGradient(startPoint, endPoint, skColors, positions, SKShaderTileMode.Clamp);
        using var gradientPaint = new SKPaint
        {
            Shader = shader,
            BlendMode = SKBlendMode.SrcIn,
            IsAntialias = false,
        };

        if (TextCompatConstants.IsDeterministicFixtureFont(SkiaCompatObjects.Typeface(font.Typeface).FamilyName)
            && !text.Contains(' '))
        {
            gradientPaint.BlendMode = SKBlendMode.SrcOver;
            skCanvas.DrawRect(Utilities.Utils.Convert(shaderRect), gradientPaint);
            skCanvas.Restore();
            return;
        }

        skCanvas.DrawRect(Utilities.Utils.Convert(shaderRect), gradientPaint);
        skCanvas.Restore();
    }

    private static PointF GetDrawOrigin(SKFont renderFont, PointF topLeft)
    {
        var metrics = renderFont.Metrics;
        return new PointF(topLeft.X, topLeft.Y - metrics.Ascent);
    }

    private static (SKPoint StartPoint, SKPoint EndPoint) GetGradientEndpoints(RectangleF rect, float angle)
    {
        var radians = angle * TextCompatConstants.DegreesToRadians;
        float cx = rect.X + rect.Width / 2f;
        float cy = rect.Y + rect.Height / 2f;
        float halfDiag = Math.Max(rect.Width, rect.Height) / 2f;
        float sin = (float)Math.Sin(radians);
        float cos = (float)Math.Cos(radians);
        return (
            new SKPoint(cx - sin * halfDiag, cy + cos * halfDiag),
            new SKPoint(cx + sin * halfDiag, cy - cos * halfDiag));
    }
}
