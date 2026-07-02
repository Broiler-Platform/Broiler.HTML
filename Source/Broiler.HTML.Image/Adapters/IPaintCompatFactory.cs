using System.Drawing;
using Broiler.Graphics;

namespace Broiler.HTML.Image.Adapters;

internal interface IPaintCompatFactory
{
    object CreateSolidBrushPaint(BColor color);

    object CreateLinearGradientBrushPaint(RectangleF rect, BColor color1, BColor color2, double angle);

    object CreatePenPaint(BColor color, float strokeWidth, Graphics.DashStyle dashStyle);

    void UpdatePenPaint(object paint, float strokeWidth, Graphics.DashStyle dashStyle);
}
