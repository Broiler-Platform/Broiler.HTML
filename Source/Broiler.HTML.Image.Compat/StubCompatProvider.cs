using System;
using Broiler.HTML.Adapters;
using Broiler.HTML.Image.Adapters;
using Broiler.HTML.Image.Adapters.Text;

namespace Broiler.HTML.Image;

/// <summary>
/// OS-free compatibility provider. The resource factory keeps the managed color
/// and SVG logic; the text, canvas, paint, path and font leaves are stubs (see
/// <see cref="StubCompatBackend"/>) pending a managed or native Broiler.Graphics
/// backend. The default "broiler" raster pipeline remains the active renderer.
/// </summary>
internal sealed class StubCompatProvider : ICompatProvider
{
    public RAdapter ImageAdapter => StubImageAdapter.Instance;

    // Real glyph rasterisation for registered fonts; falls back to the stub
    // estimate (no glyphs) for unregistered/default families.
    public ITextShaper TextShaper => TrueTypeTextShaper.Instance;

    public ICanvasCompat CanvasCompat => StubCanvasCompat.Instance;

    public IPathCompat PathCompat => StubPathCompat.Instance;

    public IFontCompatFactory FontCompatFactory => TrueTypeFontCompatFactory.Instance;

    public IPaintCompatFactory PaintCompatFactory => StubPaintCompatFactory.Instance;

    public IFontTypefaceResolver CreateFontTypefaceResolver() => new TrueTypeTypefaceResolver();

    public IBitmapCompatSurface CreateBitmapCompatSurface(
        int width,
        int height,
        Func<int, int, BColor> readPrimaryPixel,
        Action<int, int, BColor> writePrimaryPixel,
        object initialBitmap = null,
        bool ownsBitmap = true) =>
        new StubBitmapCompatSurface();
}
