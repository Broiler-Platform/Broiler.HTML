using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Broiler.HTML.Image;

/// <summary>
/// Conversion helpers between the Broiler RGBA pixel buffer convention used by
/// <see cref="BBitmap"/> and <see cref="System.Drawing.Bitmap"/>. GDI+ stores
/// 32bpp pixels as BGRA in memory (little-endian ARGB), so each conversion
/// swaps the red and blue channels.
/// </summary>
internal static class GdiPixelBuffer
{
    /// <summary>
    /// Builds a 32bpp ARGB GDI+ bitmap from a Broiler RGBA pixel buffer.
    /// </summary>
    public static Bitmap ToBitmap(int width, int height, byte[] rgba)
    {
        ArgumentNullException.ThrowIfNull(rgba);

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[width * 4];
            for (int y = 0; y < height; y++)
            {
                int srcRow = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int src = srcRow + (x * 4);
                    int dst = x * 4;
                    row[dst] = rgba[src + 2];     // B
                    row[dst + 1] = rgba[src + 1]; // G
                    row[dst + 2] = rgba[src];     // R
                    row[dst + 3] = rgba[src + 3]; // A
                }

                Marshal.Copy(row, 0, data.Scan0 + (y * data.Stride), width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    /// <summary>
    /// Reads an arbitrary image into a Broiler RGBA pixel buffer, normalizing
    /// the pixel format to 32bpp ARGB without resampling.
    /// </summary>
    public static byte[] ToRgba(System.Drawing.Image source, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(source);

        width = source.Width;
        height = source.Height;

        using var argb = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(argb))
        {
            graphics.Clear(Color.Transparent);
            graphics.DrawImageUnscaled(source, 0, 0);
        }

        return ToRgba(argb);
    }

    /// <summary>
    /// Reads a GDI+ bitmap into a Broiler RGBA pixel buffer.
    /// </summary>
    public static byte[] ToRgba(Bitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int width = source.Width;
        int height = source.Height;
        var rgba = new byte[checked(width * height * 4)];

        Bitmap argb = source;
        bool ownsArgb = false;
        if (source.PixelFormat != PixelFormat.Format32bppArgb)
        {
            argb = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            ownsArgb = true;
            using var graphics = Graphics.FromImage(argb);
            graphics.Clear(Color.Transparent);
            graphics.DrawImageUnscaled(source, 0, 0);
        }

        try
        {
            var data = argb.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[width * 4];
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, width * 4);
                    int dstRow = y * width * 4;
                    for (int x = 0; x < width; x++)
                    {
                        int src = x * 4;
                        int dst = dstRow + (x * 4);
                        rgba[dst] = row[src + 2];     // R
                        rgba[dst + 1] = row[src + 1]; // G
                        rgba[dst + 2] = row[src];     // B
                        rgba[dst + 3] = row[src + 3]; // A
                    }
                }
            }
            finally
            {
                argb.UnlockBits(data);
            }
        }
        finally
        {
            if (ownsArgb)
                argb.Dispose();
        }

        return rgba;
    }
}
