using Broiler.HTML.Adapters.Adapters;
using System.Drawing;
using System;

namespace Broiler.HTML.Image.Adapters;

internal sealed class ImageAdapter(
    BBitmap bitmap,
    bool hasIntrinsicRatio = true,
    bool hasIntrinsicWidth = true,
    bool hasIntrinsicHeight = true,
    double? intrinsicAspectRatio = null,
    double? intrinsicWidth = null,
    double? intrinsicHeight = null) : RImage
{
    public BBitmap Bitmap { get; } = bitmap;

    public override double Width => Bitmap.Width;
    public override double Height => Bitmap.Height;
    public override double IntrinsicWidth { get; } = intrinsicWidth ?? bitmap.Width;
    public override double IntrinsicHeight { get; } = intrinsicHeight ?? bitmap.Height;
    public override bool HasIntrinsicRatio { get; } = hasIntrinsicRatio;
    public override bool HasIntrinsicWidth { get; } = hasIntrinsicWidth;
    public override bool HasIntrinsicHeight { get; } = hasIntrinsicHeight;
    public override double IntrinsicAspectRatio { get; } =
        intrinsicAspectRatio.HasValue && intrinsicAspectRatio.Value > 0
            ? intrinsicAspectRatio.Value
            : (bitmap.Height > 0 ? (double)bitmap.Width / bitmap.Height : 0);

    public override bool TryGetUniformColor(out Color color)
    {
        if (Bitmap.Width <= 0 || Bitmap.Height <= 0)
        {
            color = Color.Empty;
            return false;
        }

        var first = Bitmap.GetPixel(0, 0);
        for (int y = 0; y < Bitmap.Height; y++)
        {
            for (int x = 0; x < Bitmap.Width; x++)
            {
                if (Bitmap.GetPixel(x, y) != first)
                {
                    color = Color.Empty;
                    return false;
                }
            }
        }

        color = Color.FromArgb(first.A, first.R, first.G, first.B);
        return true;
    }

    public override bool TryGetSampledColor(RectangleF sourceRect, out Color color)
    {
        if (Bitmap.Width <= 0 || Bitmap.Height <= 0)
        {
            color = Color.Empty;
            return false;
        }

        float sampleX = sourceRect.X + (sourceRect.Width / 2f);
        float sampleY = sourceRect.Y + (sourceRect.Height / 2f);
        int x = Math.Clamp((int)Math.Floor(sampleX), 0, Bitmap.Width - 1);
        int y = Math.Clamp((int)Math.Floor(sampleY), 0, Bitmap.Height - 1);
        var pixel = Bitmap.GetPixel(x, y);
        color = Color.FromArgb(pixel.A, pixel.R, pixel.G, pixel.B);
        return true;
    }

    public override void Dispose() => Bitmap.Dispose();
}
