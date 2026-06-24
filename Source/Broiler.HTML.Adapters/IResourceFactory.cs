using System.Drawing;

namespace Broiler.HTML.Adapters;

public interface IResourceFactory
{
    RPen GetPen(Color color);
    RBrush GetSolidBrush(Color color);
    RBrush GetLinearGradientBrush(RectangleF rect, Color color1, Color color2, double angle);
}
