using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Broiler.HTML.Image.Adapters;

namespace Broiler.HTML.Image;

internal sealed class GdiBitmapCompatSurface : IBitmapCompatSurface
{
    private readonly int _width;
    private readonly int _height;
    private readonly Func<int, int, BColor> _readPrimaryPixel;
    private readonly Action<int, int, BColor> _writePrimaryPixel;
    private readonly bool _ownsBitmap;
    private Bitmap? _bitmap;

    public GdiBitmapCompatSurface(
        int width,
        int height,
        Func<int, int, BColor> readPrimaryPixel,
        Action<int, int, BColor> writePrimaryPixel,
        Bitmap? initialBitmap = null,
        bool ownsBitmap = true)
    {
        _width = width;
        _height = height;
        _readPrimaryPixel = readPrimaryPixel ?? throw new ArgumentNullException(nameof(readPrimaryPixel));
        _writePrimaryPixel = writePrimaryPixel ?? throw new ArgumentNullException(nameof(writePrimaryPixel));

        if (initialBitmap is not null && !ownsBitmap)
        {
            _bitmap = new Bitmap(initialBitmap);
            _ownsBitmap = true;
        }
        else
        {
            _bitmap = initialBitmap;
            _ownsBitmap = ownsBitmap;
        }
    }

    public bool IsMaterialized => _bitmap is not null;

    public void SetPixel(int x, int y, BColor color) =>
        _bitmap?.SetPixel(x, y, Color.FromArgb(color.A, color.R, color.G, color.B));

    public void Clear(BColor color)
    {
        if (_bitmap is null)
            return;

        using var graphics = Graphics.FromImage(_bitmap);
        graphics.Clear(Color.FromArgb(color.A, color.R, color.G, color.B));
    }

    public object AsBitmap() => EnsureBitmap();

    public object ToBitmapCopy() => new Bitmap(EnsureBitmap());

    public object OpenCanvas() => new GdiCanvas(Graphics.FromImage(EnsureBitmap()));

    public void DrawPictureToFit(object picture, int width, int height)
    {
        // No "recorded picture" concept exists in the GDI+ backend; this hook is
        // unused by the current rendering paths and is kept as a safe no-op.
    }

    public void SyncToPrimaryBuffer()
    {
        if (_bitmap is null)
            return;

        var data = _bitmap.LockBits(
            new Rectangle(0, 0, _width, _height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[_width * 4];
            for (int y = 0; y < _height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, _width * 4);
                for (int x = 0; x < _width; x++)
                {
                    int offset = x * 4;
                    // GDI+ memory order is BGRA.
                    _writePrimaryPixel(x, y, new BColor(row[offset + 2], row[offset + 1], row[offset], row[offset + 3]));
                }
            }
        }
        finally
        {
            _bitmap.UnlockBits(data);
        }
    }

    public void Dispose()
    {
        if (_ownsBitmap)
            _bitmap?.Dispose();
    }

    private Bitmap EnsureBitmap()
    {
        if (_bitmap is not null)
            return _bitmap;

        var rgba = new byte[checked(_width * _height * 4)];
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                var color = _readPrimaryPixel(x, y);
                int index = ((y * _width) + x) * 4;
                rgba[index] = color.R;
                rgba[index + 1] = color.G;
                rgba[index + 2] = color.B;
                rgba[index + 3] = color.A;
            }
        }

        _bitmap = GdiPixelBuffer.ToBitmap(_width, _height, rgba);
        return _bitmap;
    }
}
