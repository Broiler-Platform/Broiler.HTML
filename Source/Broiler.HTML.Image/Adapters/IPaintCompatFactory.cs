using System.Drawing;
using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.Graphics.Rendering;

namespace Broiler.HTML.Image.Adapters;

internal interface IPaintCompatFactory
{
    object CreateSolidBrushPaint(BColor color);

    object CreateLinearGradientBrushPaint(RectangleF rect, BColor color1, BColor color2, double angle);

    object CreatePenPaint(BColor color, float strokeWidth, DashStyle dashStyle);

    void UpdatePenPaint(object paint, float strokeWidth, DashStyle dashStyle);
}
