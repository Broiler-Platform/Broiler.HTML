using System.Windows;
using System.Windows.Media.Imaging;
using Color = System.Drawing.Color;
using PointF = System.Drawing.PointF;
using SizeF = System.Drawing.SizeF;
using RectangleF = System.Drawing.RectangleF;

namespace Broiler.HTML.WPF.Utilities;

internal static class Utils
{
    public static PointF Convert(Point p) => new((float)p.X, (float)p.Y);

    public static Point Convert(PointF p) => new(p.X, p.Y);
    public static Point ConvertRound(PointF p) => new((int)p.X, (int)p.Y);
    public static SizeF Convert(Size s) => new((float)s.Width, (float)s.Height);
    public static Size Convert(SizeF s) => new(s.Width, s.Height);
    public static Size ConvertRound(SizeF s) => new((int)s.Width, (int)s.Height);
    public static RectangleF Convert(Rect r) => new((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);
    public static Rect Convert(RectangleF r) => new(r.X, r.Y, r.Width, r.Height);
    public static Rect ConvertRound(RectangleF r) => new((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
    public static Color Convert(System.Windows.Media.Color c) => Color.FromArgb(c.A, c.R, c.G, c.B);
    public static System.Windows.Media.Color Convert(Color c) => System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B);

    public static BitmapEncoder GetBitmapEncoder(string ext)
    {
        return ext.ToLower() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
            ".bmp" => new BmpBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            ".gif" => new GifBitmapEncoder(),
            ".wmp" => new WmpBitmapEncoder(),
            _ => new PngBitmapEncoder(),
        };
    }
}