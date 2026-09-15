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
/// Exposes the Save/Restore/Translate members invoked by
/// <c>CompatCanvasOperations</c> (via reflection) as no-ops.
/// </summary>
internal sealed class StubCanvas
{
    // The managed raster pipeline owns transform/clip state, so these compat
    // canvas operations are inert. They exist because CompatCanvasOperations
    // invokes them by name via reflection; the exact signatures must match its
    // call sites (parameterless Save/Restore, Translate(float, float)).
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
