using Broiler.Graphics;
using Broiler.Graphics.Color;

namespace Broiler.HTML.Image.Compat;

/// <summary>
/// OS-free bitmap compat surface. The managed raster pipeline owns the real
/// pixels, so pixel writes here are ignored and the surface never materializes a
/// platform bitmap.
/// </summary>
internal sealed class StubBitmapCompatSurface : IBitmapCompatSurface
{
    public bool IsMaterialized => false;

    public void SetPixel(int x, int y, BColor color)
    {
        // Pixels are owned by the managed raster buffer; nothing to mirror.
    }

    public void Clear(BColor color)
    {
        // See SetPixel.
    }

    public object OpenCanvas() => new StubCanvas();

    public void SyncToPrimaryBuffer()
    {
        // Never materialized, so there is nothing to sync back.
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Inert canvas returned by <see cref="StubBitmapCompatSurface.OpenCanvas"/>.
/// Implements <see cref="ICompatCanvas"/> Save/Restore/Translate operations as no-ops.
/// </summary>
internal sealed class StubCanvas : ICompatCanvas
{
    // The managed raster pipeline owns transform/clip state, so these compat
    // canvas operations are inert.
    public void Save()
    {
    }

    public void Restore()
    {
    }

    public void Translate(float x, float y)
    {
    }
}
