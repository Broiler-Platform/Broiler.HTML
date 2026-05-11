using System;
using Broiler.HTML.Adapters;
using Broiler.HTML.Image.Adapters;

namespace Broiler.HTML.Image;

internal sealed class DefaultSkiaCompatProvider : ISkiaCompatProvider
{
    public RAdapter ImageAdapter => SkiaImageAdapter.Instance;

    public ITextShaper TextShaper => SkiaTextShaper.Instance;

    public ICanvasCompat CanvasCompat => SkiaCanvasCompat.Instance;

    public IPathCompat PathCompat => SkiaPathCompat.Instance;

    public IFontCompatFactory FontCompatFactory => SkiaFontCompatFactory.Instance;

    public IPaintCompatFactory PaintCompatFactory => SkiaPaintCompatFactory.Instance;

    public IFontTypefaceResolver CreateFontTypefaceResolver() => new SkiaFontTypefaceResolver();

    public IBitmapCompatSurface CreateBitmapCompatSurface(
        int width,
        int height,
        Func<int, int, BColor> readPrimaryPixel,
        Action<int, int, BColor> writePrimaryPixel,
        object? initialBitmap = null,
        bool ownsBitmap = true) =>
        new SkiaBitmapCompatSurface(width, height, readPrimaryPixel, writePrimaryPixel, SkiaCompatObjects.BitmapOrNull(initialBitmap), ownsBitmap);
}
