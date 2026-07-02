using System;
using System.Drawing;
using Broiler.Graphics;
using Broiler.HTML.Adapters;

namespace Broiler.HTML.Image.Adapters;

internal sealed class ControlAdapter(
    PointF mouseLocation,
    bool leftMouseButton,
    bool rightMouseButton,
    Action invalidate = null,
    Action<GraphicsCursor> cursorChanged = null) : RControl(CompatProvider.ImageAdapter)
{
    private PointF _mouseLocation = mouseLocation;
    private bool _leftMouseButton = leftMouseButton;
    private bool _rightMouseButton = rightMouseButton;

    public override bool LeftMouseButton => _leftMouseButton;

    public override bool RightMouseButton => _rightMouseButton;

    public override PointF MouseLocation => _mouseLocation;

    public override void SetCursorDefault() => cursorChanged?.Invoke(GraphicsCursor.Default);

    public override void SetCursorHand() => cursorChanged?.Invoke(GraphicsCursor.Hand);

    public override void SetCursorIBeam() => cursorChanged?.Invoke(GraphicsCursor.IBeam);

    public override void DoDragDropCopy(object dragDropData)
    {
        // The platform-neutral image/graphics frontend has no drag-drop service.
    }

    public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth)
    {
        using var bitmap = new BBitmap(1, 1);
        using var graphics = bitmap.OpenGraphics(new RectangleF(0, 0, 1, 1));
        graphics.MeasureString(str, font, maxWidth, out charFit, out charFitWidth);
    }

    public override void Invalidate() => invalidate?.Invoke();
}

internal enum GraphicsCursor
{
    Default,
    Hand,
    IBeam,
}
