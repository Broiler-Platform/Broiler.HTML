using System.Drawing;
using SkiaSharp;

namespace Broiler.HTML.Image.Utilities;

internal static class Utils
{
    public static SKPoint Convert(PointF p) => new((float)p.X, (float)p.Y);
    public static SKRect Convert(RectangleF r) => SKRect.Create((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);
    public static Color Convert(SKColor c) => Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);
    public static SKColor Convert(Color c) => new(c.R, c.G, c.B, c.A);
}
