using System.Drawing;

namespace Broiler.HTML.Image.Adapters;

internal interface ITextCanvasCompat
{
    void DrawString(object canvas, FontAdapter font, object renderFont, string text, Color color, PointF point);

    void DrawGradientString(
        object canvas,
        FontAdapter font,
        object renderFont,
        string text,
        RectangleF rect,
        PointF point,
        SizeF size,
        Color[] colors,
        float[] positions,
        float angle);
}
