using System;

namespace Broiler.HTML.Adapters.Adapters;

public abstract class RGraphicsPath : IDisposable
{
    public abstract void Start(double x, double y);
    public abstract void LineTo(double x, double y);
    public abstract void ArcTo(double x, double y, double size, Corner corner);
    public abstract void Dispose();

    public enum Corner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }
}