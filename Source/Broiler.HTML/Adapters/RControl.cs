using System;
using System.Drawing;

namespace Broiler.HTML.Adapters;


public abstract class RControl
{
    protected RControl(RAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        Adapter = adapter;
    }

    public RAdapter Adapter { get; }
    public abstract bool LeftMouseButton { get; }
    public abstract bool RightMouseButton { get; }
    public abstract PointF MouseLocation { get; }
    public abstract void SetCursorDefault();
    public abstract void SetCursorHand();
    public abstract void SetCursorIBeam();
    public abstract void DoDragDropCopy(object dragDropData);
    public abstract void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth);
    public abstract void Invalidate();
}
