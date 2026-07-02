using System;
using Broiler.Graphics;
using Broiler.HTML.Adapters;

namespace Broiler.HTML.Image.Adapters;

internal sealed class PenAdapter(Func<float, Graphics.DashStyle, object> paintFactory, Action<object, float, Graphics.DashStyle> paintUpdater) : RPen
{
    private readonly Func<float, Graphics.DashStyle, object>? _paintFactory = paintFactory ?? throw new ArgumentNullException(nameof(paintFactory));
    private readonly Action<object, float, Graphics.DashStyle>? _paintUpdater = paintUpdater ?? throw new ArgumentNullException(nameof(paintUpdater));
    private object? _paint;
    private float _width = 1f;
    private Graphics.DashStyle _dashStyle = Graphics.DashStyle.Solid;

    public object Paint => _paint ??= _paintFactory?.Invoke(_width, _dashStyle)
        ?? throw new InvalidOperationException("Pen paint factory was not configured.");

    public BColor? SolidColor { get; init; }

    internal bool HasMaterializedPaint => _paint is not null;

    public bool HasSimpleStroke => SolidColor.HasValue && _dashStyle == Graphics.DashStyle.Solid;

    public override double Width
    {
        get => _width;
        set
        {
            _width = (float)value;
            if (_paint is not null)
                _paintUpdater?.Invoke(_paint, _width, _dashStyle);
        }
    }

    public override Graphics.DashStyle DashStyle
    {
        set
        {
            _dashStyle = value;
            if (_paint is not null)
                _paintUpdater?.Invoke(_paint, _width, _dashStyle);
        }
    }
}
