using System.Drawing;
using System.Drawing.Drawing2D;

namespace Broiler.HTML.Image.Adapters;

internal interface IPaintCompatFactory
{
    object CreateSolidBrushPaint(Color color);

    object CreateLinearGradientBrushPaint(RectangleF rect, Color color1, Color color2, double angle);

    object CreatePenPaint(Color color, float strokeWidth, DashStyle dashStyle);

    void UpdatePenPaint(object paint, float strokeWidth, DashStyle dashStyle);
}
