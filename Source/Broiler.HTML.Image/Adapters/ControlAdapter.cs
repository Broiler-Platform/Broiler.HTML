using System;
using System.Drawing;
using Broiler.HTML.Adapters;

namespace Broiler.HTML.Image.Adapters;

internal sealed class ControlAdapter : RControl
{
    private PointF _mouseLocation;
    private bool _leftMouseButton;
    private bool _rightMouseButton;
    private readonly Action _invalidate;
    private readonly Action<GraphicsCursor> _cursorChanged;

    public ControlAdapter(
        PointF mouseLocation,
        bool leftMouseButton,
        bool rightMouseButton,
        Action invalidate = null,
        Action<GraphicsCursor> cursorChanged = null)
        : base(CompatProvider.ImageAdapter)
    {
        _mouseLocation = mouseLocation;
        _leftMouseButton = leftMouseButton;
        _rightMouseButton = rightMouseButton;
        _invalidate = invalidate;
        _cursorChanged = cursorChanged;
    }

    public override bool LeftMouseButton => _leftMouseButton;

    public override bool RightMouseButton => _rightMouseButton;

    public override PointF MouseLocation => _mouseLocation;

    public void Update(PointF mouseLocation, bool leftMouseButton, bool rightMouseButton)
    {
        _mouseLocation = mouseLocation;
        _leftMouseButton = leftMouseButton;
        _rightMouseButton = rightMouseButton;
    }

    public override void SetCursorDefault() => _cursorChanged?.Invoke(GraphicsCursor.Default);

    public override void SetCursorHand() => _cursorChanged?.Invoke(GraphicsCursor.Hand);

    public override void SetCursorIBeam() => _cursorChanged?.Invoke(GraphicsCursor.IBeam);

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

    public override void Invalidate() => _invalidate?.Invoke();
}

internal enum GraphicsCursor
{
    Default,
    Hand,
    IBeam,
}
