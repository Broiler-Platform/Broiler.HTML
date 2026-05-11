using System.Drawing;

namespace Broiler.HTML.Image.Adapters;

internal interface ICanvasCompat
{
    void PushClip(object canvas, RectangleF rect);

    void PushClipExclude(object canvas, RectangleF rect);

    void DrawLine(object canvas, float x1, float y1, float x2, float y2, object paint);

    void DrawRectangle(object canvas, RectangleF rect, object paint);

    void DrawImage(object canvas, BBitmap bitmap, RectangleF destRect, RectangleF srcRect);

    void DrawImage(object canvas, BBitmap bitmap, RectangleF destRect);

    void DrawPath(object canvas, GraphicsPathAdapter path, object paint);

    void ClipRounded(
        object canvas,
        RectangleF rect,
        double cornerNw,
        double cornerNwY,
        double cornerNe,
        double cornerNeY,
        double cornerSe,
        double cornerSeY,
        double cornerSw,
        double cornerSwY);

    object CreateTexturePaint(BBitmap bitmap, PointF translateTransformLocation);

    void DrawPolygon(object canvas, PointF[] points, object paint);

    void SaveOpacityLayer(object canvas, float opacity);

    void SaveBlendLayer(object canvas, string blendMode);
}
