using System;
using System.Drawing;

namespace Broiler.HTML.Image.Adapters;

internal sealed class GdiPathCompat : IPathCompat
{
    public static IPathCompat Instance { get; } = new GdiPathCompat();

    public object CreatePath() => new GdiPathState();

    public void Reset(object path)
    {
        var state = GdiCompatObjects.Path(path);
        state.Path.Reset();
        state.HasCurrent = false;
    }

    public void MoveTo(object path, float x, float y)
    {
        var state = GdiCompatObjects.Path(path);
        state.Path.StartFigure();
        state.Current = new PointF(x, y);
        state.HasCurrent = true;
    }

    public void LineTo(object path, float x, float y)
    {
        var state = GdiCompatObjects.Path(path);
        if (state.HasCurrent)
            state.Path.AddLine(state.Current.X, state.Current.Y, x, y);

        state.Current = new PointF(x, y);
        state.HasCurrent = true;
    }

    public void ArcTo(object path, float left, float top, float width, float height, float startAngle, float sweepAngle, bool forceMoveTo)
    {
        var state = GdiCompatObjects.Path(path);
        if (width <= 0 || height <= 0)
            return;

        state.Path.AddArc(left, top, width, height, startAngle, sweepAngle);

        double endAngle = (startAngle + sweepAngle) * Math.PI / 180.0;
        float cx = left + (width / 2f);
        float cy = top + (height / 2f);
        state.Current = new PointF(
            cx + ((width / 2f) * (float)Math.Cos(endAngle)),
            cy + ((height / 2f) * (float)Math.Sin(endAngle)));
        state.HasCurrent = true;
    }
}
