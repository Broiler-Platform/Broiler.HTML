using System.Drawing;
using System;
using Broiler.Graphics;

namespace Broiler.HTML.Image.Adapters;

internal sealed class BrushAdapter : RBrush
{
    private readonly Func<object>? _paintFactory;
    private readonly bool _dispose;
    private object? _paint;

    public BrushAdapter(object paint, bool dispose)
    {
        _paint = paint ?? throw new ArgumentNullException(nameof(paint));
        _dispose = dispose;
    }

    public BrushAdapter(Func<object> paintFactory, bool dispose)
    {
        _paintFactory = paintFactory ?? throw new ArgumentNullException(nameof(paintFactory));
        _dispose = dispose;
    }

    public object Paint => _paint ??= _paintFactory?.Invoke()
        ?? throw new InvalidOperationException("Brush paint factory was not configured.");

    public BColor? SolidColor { get; init; }

    public BBitmap? TextureBitmap { get; init; }

    public RectangleF? TextureSourceRect { get; init; }

    public PointF? TextureOrigin { get; init; }

    internal bool HasMaterializedPaint => _paint is not null;

    public override void Dispose()
    {
        if (_dispose)
            (_paint as IDisposable)?.Dispose();
    }
}
