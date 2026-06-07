using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Broiler.HTML.Image.Adapters;

namespace Broiler.HTML.Image;

/// <summary>
/// Broiler-owned bitmap abstraction that routes rendering through the
/// currently selected migration backend.
/// </summary>
public sealed class BBitmap : IDisposable
{
    private readonly byte[] _pixels;
    private readonly IBitmapCompatSurface _compatSurface;

    public BBitmap(int width, int height)
    {
        ValidateDimensions(width, height);
        Width = width;
        Height = height;
        _pixels = new byte[checked(width * height * 4)];
        _compatSurface = CreateDefaultCompatSurface();
    }

    internal BBitmap(int width, int height, byte[] pixels, IBitmapCompatSurface? compatSurface = null)
    {
        ValidateDimensions(width, height);

        Width = width;
        Height = height;
        _pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
        _compatSurface = compatSurface ?? CreateDefaultCompatSurface();
    }

    public int Width { get; }

    public int Height { get; }

    public BColor GetPixel(int x, int y)
    {
        return ReadPrimaryPixel(x, y);
    }

    public void SetPixel(int x, int y, BColor color)
    {
        WritePrimaryPixel(x, y, color);
        _compatSurface.SetPixel(x, y, color);
    }

    public void Clear(BColor color) => ErasePixels(color);

    internal void ErasePixels(BColor color)
    {
        for (int i = 0; i < _pixels.Length; i += 4)
        {
            _pixels[i] = color.R;
            _pixels[i + 1] = color.G;
            _pixels[i + 2] = color.B;
            _pixels[i + 3] = color.A;
        }

        _compatSurface.Clear(color);
    }

    internal void Erase(BColor color) => Clear(color);

    public byte[] Encode(BImageFormat format = BImageFormat.Png, int quality = 100)
    {
        using var image = CreateGdiBitmap();
        using var stream = new MemoryStream();
        SaveGdiBitmap(image, stream, format, quality);
        return stream.ToArray();
    }

    public void Save(string filePath, BImageFormat format = BImageFormat.Png, int quality = 100)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        using var image = CreateGdiBitmap();
        using var stream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        SaveGdiBitmap(image, stream, format, quality);
    }

    public BBitmap Copy() => new(Width, Height, (byte[])_pixels.Clone());

    internal BBitmap ResizeNearest(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        if (width == Width && height == Height)
            return Copy();

        var resized = new BBitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            int srcY = Math.Min(Height - 1, (int)((long)y * Height / height));
            for (int x = 0; x < width; x++)
            {
                int srcX = Math.Min(Width - 1, (int)((long)x * Width / width));
                resized.SetPixel(x, y, GetPixel(srcX, srcY));
            }
        }

        return resized;
    }

    public static BBitmap Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var stream = new MemoryStream(data, writable: false);
        using var image = System.Drawing.Image.FromStream(stream);
        return FromGdiImage(image);
    }

    public static BBitmap Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var image = System.Drawing.Image.FromStream(stream);
        return FromGdiImage(image);
    }

    public static BBitmap Decode(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using var image = System.Drawing.Image.FromFile(path);
        return FromGdiImage(image);
    }

    internal bool HasMaterializedCompatBitmap => _compatSurface.IsMaterialized;
    internal int CompatSyncInvocationCount { get; private set; }

    internal object OpenCanvas() => _compatSurface.OpenCanvas();

    internal GraphicsAdapter OpenGraphics(RectangleF clip)
    {
        var rasterCanvas = BGraphicsBackend.UseBroilerRasterPipeline ? OpenRasterCanvas() : null;
        return new GraphicsAdapter(OpenCanvas, clip, rasterCanvas, disposeCanvas: true, onDispose: SyncPixelsFromCompatBitmapIfMaterialized);
    }

    internal BCanvas OpenRasterCanvas() => new(this);

    internal GraphicsAdapter OpenGraphics(RectangleF clip, PointF translation)
    {
        var rasterCanvas = BGraphicsBackend.UseBroilerRasterPipeline ? OpenRasterCanvas() : null;
        if (rasterCanvas is not null)
        {
            rasterCanvas.Save();
            rasterCanvas.Translate(translation.X, translation.Y);
        }

        return new GraphicsAdapter(
            OpenCanvas,
            clip,
            rasterCanvas,
            disposeCanvas: true,
            restoreOnDispose: true,
            onDispose: SyncPixelsFromCompatBitmapIfMaterialized,
            initialCanvasOperation: static (canvas, state) =>
            {
                var offset = (PointF)state;
                CompatCanvasOperations.Save(canvas);
                CompatCanvasOperations.Translate(canvas, offset.X, offset.Y);
            },
            initialCanvasOperationState: translation);
    }

    internal void DrawPictureToFit(object picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        _compatSurface.DrawPictureToFit(picture, Width, Height);
        SyncPixelsFromCompatBitmap();
    }

    internal object AsCompatBitmap() => EnsureCompatBitmap();

    internal object ToCompatBitmapCopy() => _compatSurface.ToBitmapCopy();

    public void Dispose() => _compatSurface.Dispose();

    private int GetPixelIndex(int x, int y) => checked(((y * Width) + x) * 4);

    private static void SaveGdiBitmap(Bitmap image, Stream stream, BImageFormat format, int quality)
    {
        if (format == BImageFormat.Jpeg)
        {
            var encoder = ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(static codec => codec.FormatID == ImageFormat.Jpeg.Guid);
            if (encoder is not null)
            {
                using var parameters = new EncoderParameters(1);
                parameters.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(quality, 1L, 100L));
                image.Save(stream, encoder, parameters);
                return;
            }

            image.Save(stream, ImageFormat.Jpeg);
            return;
        }

        image.Save(stream, ImageFormat.Png);
    }

    private Bitmap CreateGdiBitmap() => GdiPixelBuffer.ToBitmap(Width, Height, _pixels);

    private object EnsureCompatBitmap() => _compatSurface.AsBitmap();

    /// <summary>
    /// Creates a Broiler bitmap from a GDI+ image, copying its pixels into the
    /// primary RGBA buffer.
    /// </summary>
    internal static BBitmap FromGdiImage(System.Drawing.Image image)
    {
        var pixels = GdiPixelBuffer.ToRgba(image, out int width, out int height);
        return new BBitmap(width, height, pixels);
    }

    private void SyncPixelsFromCompatBitmapIfMaterialized()
    {
        if (!HasMaterializedCompatBitmap)
            return;

        SyncPixelsFromCompatBitmap();
    }

    private void SyncPixelsFromCompatBitmap()
    {
        CompatSyncInvocationCount++;
        _compatSurface.SyncToPrimaryBuffer();
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
    }

    private IBitmapCompatSurface CreateDefaultCompatSurface(object? initialBitmap = null, bool ownsBitmap = true)
        => CompatProvider.CreateBitmapCompatSurface(Width, Height, ReadPrimaryPixel, WritePrimaryPixel, initialBitmap, ownsBitmap);

    private BColor ReadPrimaryPixel(int x, int y)
    {
        int index = GetPixelIndex(x, y);
        return new BColor(_pixels[index], _pixels[index + 1], _pixels[index + 2], _pixels[index + 3]);
    }

    private void WritePrimaryPixel(int x, int y, BColor color)
    {
        int index = GetPixelIndex(x, y);
        _pixels[index] = color.R;
        _pixels[index + 1] = color.G;
        _pixels[index + 2] = color.B;
        _pixels[index + 3] = color.A;
    }
}
