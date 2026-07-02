using System;
using Broiler.Graphics;

namespace Broiler.HTML.Image.Compat;

/// <summary>
/// OS-free bitmap compat surface. The managed raster pipeline owns the real
/// pixels, so pixel writes here are ignored and the surface never materializes a
/// platform bitmap. Materialization entry points throw, since they cannot be
/// satisfied without an OS/native graphics backend.
/// </summary>
internal sealed class StubBitmapCompatSurface : IBitmapCompatSurface
{
    private const string Message =
        "No OS graphics backend is available to materialize a platform bitmap; " +
        "the managed Broiler raster pipeline is the active renderer.";

    public bool IsMaterialized => false;

    public void SetPixel(int x, int y, BColor color)
    {
        // Pixels are owned by the managed raster buffer; nothing to mirror.
    }

    public void Clear(BColor color)
    {
        // See SetPixel.
    }

    public object AsBitmap() => throw new NotSupportedException(Message);

    public object ToBitmapCopy() => throw new NotSupportedException(Message);

    public object OpenCanvas() => new StubCanvas();

    public void DrawPictureToFit(object picture, int width, int height) => throw new NotSupportedException(Message);

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
}
