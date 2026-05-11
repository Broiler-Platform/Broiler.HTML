using SkiaSharp;

namespace Broiler.HTML.Image.Adapters;

internal sealed class SkiaPathCompat : IPathCompat
{
    public static IPathCompat Instance { get; } = new SkiaPathCompat();

    public object CreatePath() => new SKPath();

    public void Reset(object path) => SkiaCompatObjects.Path(path).Reset();

    public void MoveTo(object path, float x, float y) => SkiaCompatObjects.Path(path).MoveTo(x, y);

    public void LineTo(object path, float x, float y) => SkiaCompatObjects.Path(path).LineTo(x, y);

    public void ArcTo(object path, float left, float top, float width, float height, float startAngle, float sweepAngle, bool forceMoveTo) =>
        SkiaCompatObjects.Path(path).ArcTo(SKRect.Create(left, top, width, height), startAngle, sweepAngle, forceMoveTo);
}
