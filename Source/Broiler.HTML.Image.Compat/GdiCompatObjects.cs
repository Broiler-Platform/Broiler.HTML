using System;
using System.Drawing;
using Broiler.HTML.Image.Adapters;

namespace Broiler.HTML.Image;

/// <summary>
/// Casting helpers between the opaque <c>object</c> handles used by the
/// compatibility interfaces and the concrete GDI+ backend types.
/// </summary>
internal static class GdiCompatObjects
{
    internal static Bitmap Bitmap(object bitmap) => (Bitmap)bitmap;

    internal static Bitmap? BitmapOrNull(object? bitmap) => bitmap as Bitmap;

    internal static GdiCanvas Canvas(object canvas) => (GdiCanvas)canvas;

    internal static GdiPaint Paint(object paint) => (GdiPaint)paint;

    internal static GdiPathState Path(object path) => (GdiPathState)path;

    internal static Font Font(object font) => (Font)font;

    internal static GdiTypeface Typeface(object typeface) => (GdiTypeface)typeface;

    internal static BBitmap CreateBitmap(Bitmap bitmap, bool ownsBitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var pixels = GdiPixelBuffer.ToRgba(bitmap);
        int width = bitmap.Width;
        int height = bitmap.Height;

        BColor ReadPrimaryPixel(int x, int y)
        {
            int index = ((y * width) + x) * 4;
            return new BColor(pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]);
        }

        void WritePrimaryPixel(int x, int y, BColor color)
        {
            int index = ((y * width) + x) * 4;
            pixels[index] = color.R;
            pixels[index + 1] = color.G;
            pixels[index + 2] = color.B;
            pixels[index + 3] = color.A;
        }

        var compatSurface = new GdiBitmapCompatSurface(width, height, ReadPrimaryPixel, WritePrimaryPixel, bitmap, ownsBitmap);
        return new BBitmap(width, height, pixels, compatSurface);
    }
}
