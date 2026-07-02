using System;
using Broiler.Graphics;

namespace Broiler.HTML.Image;

internal interface IBitmapCompatSurface : IDisposable
{
    bool IsMaterialized { get; }

    void SetPixel(int x, int y, BColor color);

    void Clear(BColor color);

    object AsBitmap();

    object ToBitmapCopy();

    object OpenCanvas();

    void DrawPictureToFit(object picture, int width, int height);

    void SyncToPrimaryBuffer();
}
