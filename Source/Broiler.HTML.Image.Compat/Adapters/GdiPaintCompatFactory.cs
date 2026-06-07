using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Broiler.HTML.Image.Adapters;

internal sealed class GdiPaintCompatFactory : IPaintCompatFactory
{
    public static IPaintCompatFactory Instance { get; } = new GdiPaintCompatFactory();

    public object CreateSolidBrushPaint(Color color) =>
        new GdiPaint { Brush = new SolidBrush(color) };

    public object CreateLinearGradientBrushPaint(RectangleF rect, Color color1, Color color2, double angle)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return new GdiPaint { Brush = new SolidBrush(color1) };

        // GDI+ measures the gradient angle clockwise from the positive X axis,
        // matching the CSS-derived angle used elsewhere.
        var brush = new LinearGradientBrush(rect, color1, color2, (float)angle)
        {
            WrapMode = WrapMode.TileFlipXY,
        };
        return new GdiPaint { Brush = brush };
    }

    public object CreatePenPaint(Color color, float strokeWidth, DashStyle dashStyle)
    {
        var pen = new Pen(color, strokeWidth <= 0 ? 1f : strokeWidth);
        var paint = new GdiPaint { Pen = pen };
        UpdatePenPaint(paint, strokeWidth, dashStyle);
        return paint;
    }

    public void UpdatePenPaint(object paint, float strokeWidth, DashStyle dashStyle)
    {
        var pen = GdiCompatObjects.Paint(paint).Pen;
        if (pen is null)
            return;

        pen.Width = strokeWidth <= 0 ? 1f : strokeWidth;
        ApplyDash(pen, dashStyle, pen.Width);
    }

    private static void ApplyDash(Pen pen, DashStyle dashStyle, float strokeWidth)
    {
        switch (dashStyle)
        {
            case DashStyle.Solid:
                pen.DashStyle = DashStyle.Solid;
                break;
            case DashStyle.Dash:
                pen.DashStyle = DashStyle.Custom;
                // Match Chromium's dashed border cadence for CSS border rendering.
                pen.DashPattern = strokeWidth < 2f ? [4f, 4f] : [2f, 1f];
                break;
            case DashStyle.Dot:
                pen.DashStyle = DashStyle.Custom;
                pen.DashPattern = [1f, 1f];
                break;
            case DashStyle.DashDot:
                pen.DashStyle = DashStyle.Custom;
                pen.DashPattern = [4f, 2f, 1f, 2f];
                break;
            case DashStyle.DashDotDot:
                pen.DashStyle = DashStyle.Custom;
                pen.DashPattern = [4f, 2f, 1f, 2f, 1f, 2f];
                break;
            default:
                pen.DashStyle = DashStyle.Solid;
                break;
        }
    }
}
