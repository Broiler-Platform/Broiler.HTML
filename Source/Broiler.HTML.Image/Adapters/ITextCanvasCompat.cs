using System.Drawing;
using Broiler.Graphics;
using Broiler.Graphics.Color;

namespace Broiler.HTML.Image.Adapters;

internal interface ITextCanvasCompat
{
    void DrawString(object canvas, FontAdapter font, object renderFont, string text, BColor color, PointF point);

    void DrawGradientString(
        object canvas,
        FontAdapter font,
        object renderFont,
        string text,
        RectangleF rect,
        PointF point,
        SizeF size,
        BColor[] colors,
        float[] positions,
        float angle);
}
