using System;
using Broiler.Graphics;
using Broiler.Graphics.Color;

namespace Broiler.HTML.Image;

internal interface IBitmapCompatSurface : IDisposable
{
    bool IsMaterialized { get; }

    void SetPixel(int x, int y, BColor color);

    void Clear(BColor color);

    object OpenCanvas();

    void SyncToPrimaryBuffer();
}
