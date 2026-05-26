using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using SkiaSharp;

namespace Broiler.HTML.Image.Adapters;

internal sealed class SkiaPaintCompatFactory : IPaintCompatFactory
{
    public static IPaintCompatFactory Instance { get; } = new SkiaPaintCompatFactory();

    public object CreateSolidBrushPaint(Color color) =>
        new SKPaint
        {
            Color = Utilities.Utils.Convert(color),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

    public object CreateLinearGradientBrushPaint(RectangleF rect, Color color1, Color color2, double angle)
    {
        var radians = angle * Math.PI / 180.0;
        var cos = (float)Math.Cos(radians);
        var sin = (float)Math.Sin(radians);
        var cx = (float)(rect.X + rect.Width / 2);
        var cy = (float)(rect.Y + rect.Height / 2);
        var halfDiag = (float)Math.Max(rect.Width, rect.Height) / 2;
        var colors = new SKColor[]
        {
            Utilities.Utils.Convert(color1),
            Utilities.Utils.Convert(color2),
        };

        var startPoint = new SKPoint(cx - cos * halfDiag, cy - sin * halfDiag);
        var endPoint = new SKPoint(cx + cos * halfDiag, cy + sin * halfDiag);

        return new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                startPoint,
                endPoint,
                colors,
                null,
                SKShaderTileMode.Clamp),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
    }

    public object CreatePenPaint(Color color, float strokeWidth, DashStyle dashStyle)
    {
        var paint = new SKPaint
        {
            Color = Utilities.Utils.Convert(color),
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };
        UpdatePenPaint(paint, strokeWidth, dashStyle);
        return paint;
    }

    public void UpdatePenPaint(object paint, float strokeWidth, DashStyle dashStyle)
    {
        var skPaint = SkiaCompatObjects.Paint(paint);
        skPaint.StrokeWidth = strokeWidth;
        skPaint.PathEffect = CreatePathEffect(dashStyle, strokeWidth);
    }

    private static SKPathEffect? CreatePathEffect(DashStyle dashStyle, float strokeWidth) => dashStyle switch
    {
        DashStyle.Solid => null,
        DashStyle.Dash => strokeWidth < 2f
            ? SKPathEffect.CreateDash([4f, 4f], 0)
            // Match Chromium's dashed border cadence more closely for CSS
            // border rendering and the corresponding WPT references.
            : SKPathEffect.CreateDash([2f * strokeWidth, strokeWidth], 0),
        DashStyle.Dot => SKPathEffect.CreateDash([strokeWidth, strokeWidth], 0),
        DashStyle.DashDot => SKPathEffect.CreateDash([4f * strokeWidth, 2f * strokeWidth, strokeWidth, 2f * strokeWidth], 0),
        DashStyle.DashDotDot => SKPathEffect.CreateDash([4f * strokeWidth, 2f * strokeWidth, strokeWidth, 2f * strokeWidth, strokeWidth, 2f * strokeWidth], 0),
        _ => null,
    };
}
