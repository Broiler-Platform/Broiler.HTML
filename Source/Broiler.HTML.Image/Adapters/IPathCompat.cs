namespace Broiler.HTML.Image.Adapters;

internal interface IPathCompat
{
    object CreatePath();
    void Reset(object path);
    void MoveTo(object path, float x, float y);
    void LineTo(object path, float x, float y);
    void ArcTo(object path, float left, float top, float width, float height, float startAngle, float sweepAngle, bool forceMoveTo);
}
