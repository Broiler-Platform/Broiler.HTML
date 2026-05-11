using System;
using System.Drawing;
using System.IO;
using Broiler.HTML.Image.Adapters;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

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
        using var image = CreateImageSharpImage();
        using var stream = new MemoryStream();
        image.Save(stream, CreateEncoder(format, quality));
        return stream.ToArray();
    }

    public void Save(string filePath, BImageFormat format = BImageFormat.Png, int quality = 100)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        using var image = CreateImageSharpImage();
        using var stream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        image.Save(stream, CreateEncoder(format, quality));
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

        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(data);
        return CreateFromImageSharpImage(image);
    }

    public static BBitmap Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
        return CreateFromImageSharpImage(image);
    }

    public static BBitmap Decode(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
        return CreateFromImageSharpImage(image);
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

    private IImageEncoder CreateEncoder(BImageFormat format, int quality) => format switch
    {
        BImageFormat.Jpeg => new JpegEncoder
        {
            Quality = Math.Clamp(quality, 1, 100),
        },
        _ => new PngEncoder(),
    };

    private SixLabors.ImageSharp.Image<Rgba32> CreateImageSharpImage()
    {
        var image = new SixLabors.ImageSharp.Image<Rgba32>(Width, Height);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int index = GetPixelIndex(x, y);
                image[x, y] = new Rgba32(_pixels[index], _pixels[index + 1], _pixels[index + 2], _pixels[index + 3]);
            }
        }

        return image;
    }

    private object EnsureCompatBitmap() => _compatSurface.AsBitmap();

    internal static BBitmap CreateFromImageSharpImage(SixLabors.ImageSharp.Image<Rgba32> image)
    {
        var pixels = new byte[checked(image.Width * image.Height * 4)];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                var color = image[x, y];
                int index = ((y * image.Width) + x) * 4;
                pixels[index] = color.R;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.B;
                pixels[index + 3] = color.A;
            }
        }

        return new BBitmap(image.Width, image.Height, pixels);
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
        => SkiaCompatProvider.CreateBitmapCompatSurface(Width, Height, ReadPrimaryPixel, WritePrimaryPixel, initialBitmap, ownsBitmap);

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
