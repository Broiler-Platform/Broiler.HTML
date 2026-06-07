using System;
using Broiler.HTML.Adapters;
using Broiler.HTML.Image.Adapters;

namespace Broiler.HTML.Image;

internal sealed class GdiCompatProvider : ICompatProvider
{
    public RAdapter ImageAdapter => GdiImageAdapter.Instance;

    public ITextShaper TextShaper => GdiTextShaper.Instance;

    public ICanvasCompat CanvasCompat => GdiCanvasCompat.Instance;

    public IPathCompat PathCompat => GdiPathCompat.Instance;

    public IFontCompatFactory FontCompatFactory => GdiFontCompatFactory.Instance;

    public IPaintCompatFactory PaintCompatFactory => GdiPaintCompatFactory.Instance;

    public IFontTypefaceResolver CreateFontTypefaceResolver() => new GdiFontTypefaceResolver();

    public IBitmapCompatSurface CreateBitmapCompatSurface(
        int width,
        int height,
        Func<int, int, BColor> readPrimaryPixel,
        Action<int, int, BColor> writePrimaryPixel,
        object? initialBitmap = null,
        bool ownsBitmap = true) =>
        new GdiBitmapCompatSurface(width, height, readPrimaryPixel, writePrimaryPixel, GdiCompatObjects.BitmapOrNull(initialBitmap), ownsBitmap);
}
