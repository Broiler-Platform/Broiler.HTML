using System;
using SkiaSharp;

namespace Broiler.HTML.Image;

internal sealed class SkiaBitmapCompatSurface : IBitmapCompatSurface
{
    private readonly int _width;
    private readonly int _height;
    private readonly Func<int, int, BColor> _readPrimaryPixel;
    private readonly Action<int, int, BColor> _writePrimaryPixel;
    private readonly bool _ownsBitmap;
    private SKBitmap? _bitmap;

    public SkiaBitmapCompatSurface(
        int width,
        int height,
        Func<int, int, BColor> readPrimaryPixel,
        Action<int, int, BColor> writePrimaryPixel,
        SKBitmap? initialBitmap = null,
        bool ownsBitmap = true)
    {
        _width = width;
        _height = height;
        _readPrimaryPixel = readPrimaryPixel ?? throw new ArgumentNullException(nameof(readPrimaryPixel));
        _writePrimaryPixel = writePrimaryPixel ?? throw new ArgumentNullException(nameof(writePrimaryPixel));

        if (initialBitmap is not null && !ownsBitmap)
        {
            _bitmap = initialBitmap.Copy();
            _ownsBitmap = true;
        }
        else
        {
            _bitmap = initialBitmap;
            _ownsBitmap = ownsBitmap;
        }
    }

    public bool IsMaterialized => _bitmap is not null;

    public void SetPixel(int x, int y, BColor color)
    {
        _bitmap?.SetPixel(x, y, new SKColor(color.R, color.G, color.B, color.A));
    }

    public void Clear(BColor color)
    {
        _bitmap?.Erase(new SKColor(color.R, color.G, color.B, color.A));
    }

    public object AsBitmap() => EnsureBitmap();

    public object ToBitmapCopy() => EnsureBitmap().Copy();

    public object OpenCanvas() => new SKCanvas(EnsureBitmap());

    public void DrawPictureToFit(object picture, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(picture);
        var skPicture = SkiaCompatObjects.Picture(picture);

        using var canvas = new SKCanvas(EnsureBitmap());
        var cullRect = skPicture.CullRect;
        if (cullRect.Width > 0 && cullRect.Height > 0
            && ((int)Math.Ceiling(cullRect.Width) != width
                || (int)Math.Ceiling(cullRect.Height) != height))
        {
            float scaleX = width / cullRect.Width;
            float scaleY = height / cullRect.Height;
            canvas.Scale(scaleX, scaleY);
        }

        canvas.DrawPicture(skPicture);
    }

    public void SyncToPrimaryBuffer()
    {
        if (_bitmap is null)
            return;

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                var color = _bitmap.GetPixel(x, y);
                _writePrimaryPixel(x, y, new BColor(color.Red, color.Green, color.Blue, color.Alpha));
            }
        }
    }

    public void Dispose()
    {
        if (_ownsBitmap)
            _bitmap?.Dispose();
    }

    private SKBitmap EnsureBitmap()
    {
        if (_bitmap is not null)
            return _bitmap;

        var bitmap = new SKBitmap(_width, _height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                var color = _readPrimaryPixel(x, y);
                bitmap.SetPixel(x, y, new SKColor(color.R, color.G, color.B, color.A));
            }
        }

        _bitmap = bitmap;
        return bitmap;
    }
}
