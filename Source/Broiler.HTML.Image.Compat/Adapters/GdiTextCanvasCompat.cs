using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Broiler.HTML.Image.Adapters;

internal sealed class GdiTextCanvasCompat : ITextCanvasCompat
{
    public static ITextCanvasCompat Instance { get; } = new GdiTextCanvasCompat();

    public void DrawString(object canvas, FontAdapter font, object renderFont, string text, Color color, PointF point)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var graphics = GdiCompatObjects.Canvas(canvas).Graphics;
        using var format = GdiTextMetricsCompat.CreateMeasureFormat();
        using var brush = new SolidBrush(color);
        graphics.DrawString(text, (Font)renderFont, brush, point.X, point.Y, format);
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
        if (string.IsNullOrEmpty(text) || colors is null || colors.Length == 0)
            return;

        var graphics = GdiCompatObjects.Canvas(canvas).Graphics;
        var gdiRenderFont = (Font)renderFont;
        using var format = GdiTextMetricsCompat.CreateMeasureFormat();

        if (colors.Length == 1)
        {
            using var solid = new SolidBrush(colors[0]);
            graphics.DrawString(text, gdiRenderFont, solid, point.X, point.Y, format);
            return;
        }

        float shaderWidth = Math.Max(rect.Width, GdiTextMetricsCompat.MeasureRaw(gdiRenderFont, text));
        float shaderHeight = Math.Max(rect.Height > 0 ? rect.Height : size.Height, (float)font.Size);
        var shaderRect = new RectangleF(rect.X, rect.Y, Math.Max(1f, shaderWidth), Math.Max(1f, shaderHeight));

        using var brush = TextGradientBrush.Create(shaderRect, colors, positions, angle);
        graphics.DrawString(text, gdiRenderFont, brush, point.X, point.Y, format);
    }
}

/// <summary>
/// Builds GDI+ linear gradient brushes for gradient-filled text/shapes,
/// translating the CSS-style angle and colour-stop list to GDI+ semantics.
/// </summary>
internal static class TextGradientBrush
{
    public static Brush Create(RectangleF rect, Color[] colors, float[] positions, float angle)
    {
        var (start, end) = GetGradientEndpoints(rect, angle);
        if (Math.Abs(start.X - end.X) < 0.01f && Math.Abs(start.Y - end.Y) < 0.01f)
            return new SolidBrush(colors[0]);

        var brush = new LinearGradientBrush(start, end, colors[0], colors[^1])
        {
            WrapMode = WrapMode.TileFlipXY,
        };

        if (colors.Length > 1)
        {
            var blend = new ColorBlend(colors.Length)
            {
                Colors = colors,
                Positions = NormalizePositions(colors.Length, positions),
            };
            brush.InterpolationColors = blend;
        }

        return brush;
    }

    private static float[] NormalizePositions(int count, float[] positions)
    {
        var result = new float[count];
        if (positions is not null && positions.Length == count)
        {
            for (int i = 0; i < count; i++)
                result[i] = Math.Clamp(positions[i], 0f, 1f);
        }
        else if (count == 1)
        {
            result[0] = 0f;
        }
        else
        {
            for (int i = 0; i < count; i++)
                result[i] = (float)i / (count - 1);
        }

        // GDI+ requires strictly non-decreasing positions starting at 0 and ending at 1.
        result[0] = 0f;
        result[^1] = 1f;
        for (int i = 1; i < count; i++)
        {
            if (result[i] < result[i - 1])
                result[i] = result[i - 1];
        }

        return result;
    }

    private static (PointF Start, PointF End) GetGradientEndpoints(RectangleF rect, float angle)
    {
        double radians = angle * Math.PI / 180.0;
        float cx = rect.X + (rect.Width / 2f);
        float cy = rect.Y + (rect.Height / 2f);
        float halfDiag = Math.Max(rect.Width, rect.Height) / 2f;
        float sin = (float)Math.Sin(radians);
        float cos = (float)Math.Cos(radians);
        return (
            new PointF(cx - (sin * halfDiag), cy + (cos * halfDiag)),
            new PointF(cx + (sin * halfDiag), cy - (cos * halfDiag)));
    }
}
