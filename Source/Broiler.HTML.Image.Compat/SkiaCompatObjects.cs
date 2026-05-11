using System;
using SkiaSharp;

namespace Broiler.HTML.Image;

internal static class SkiaCompatObjects
{
    internal static SKBitmap Bitmap(object bitmap) => (SKBitmap)bitmap;

    internal static SKBitmap? BitmapOrNull(object? bitmap) => bitmap is null ? null : (SKBitmap)bitmap;

    internal static SKCanvas Canvas(object canvas) => (SKCanvas)canvas;

    internal static SKPaint Paint(object paint) => (SKPaint)paint;

    internal static SKPath Path(object path) => (SKPath)path;

    internal static SKFont Font(object font) => (SKFont)font;

    internal static SKTypeface Typeface(object typeface) => (SKTypeface)typeface;

    internal static SKPicture Picture(object picture) => (SKPicture)picture;

    internal static BBitmap CreateBitmap(SKBitmap bitmap, bool ownsBitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var pixels = new byte[checked(bitmap.Width * bitmap.Height * 4)];
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                int index = ((y * bitmap.Width) + x) * 4;
                pixels[index] = color.Red;
                pixels[index + 1] = color.Green;
                pixels[index + 2] = color.Blue;
                pixels[index + 3] = color.Alpha;
            }
        }

        BColor ReadPrimaryPixel(int x, int y)
        {
            int index = ((y * bitmap.Width) + x) * 4;
            return new BColor(pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]);
        }

        void WritePrimaryPixel(int x, int y, BColor color)
        {
            int index = ((y * bitmap.Width) + x) * 4;
            pixels[index] = color.R;
            pixels[index + 1] = color.G;
            pixels[index + 2] = color.B;
            pixels[index + 3] = color.A;
        }

        var compatSurface = new SkiaBitmapCompatSurface(bitmap.Width, bitmap.Height, ReadPrimaryPixel, WritePrimaryPixel, bitmap, ownsBitmap);
        return new BBitmap(bitmap.Width, bitmap.Height, pixels, compatSurface);
    }
}
