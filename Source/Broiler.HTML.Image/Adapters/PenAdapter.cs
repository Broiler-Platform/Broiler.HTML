using System;
using Broiler.Graphics;
using Broiler.Graphics.Adapters;
using Broiler.Graphics.Color;
using Broiler.Graphics.Rendering;

namespace Broiler.HTML.Image.Adapters;

internal sealed class PenAdapter(Func<float, DashStyle, object> paintFactory, Action<object, float, DashStyle> paintUpdater) : BPen
{
    private readonly Func<float, DashStyle, object>? _paintFactory = paintFactory ?? throw new ArgumentNullException(nameof(paintFactory));
    private readonly Action<object, float, DashStyle>? _paintUpdater = paintUpdater ?? throw new ArgumentNullException(nameof(paintUpdater));
    private object? _paint;
    private float _width = 1f;
    private DashStyle _dashStyle = DashStyle.Solid;

    public object Paint => _paint ??= _paintFactory?.Invoke(_width, _dashStyle)
        ?? throw new InvalidOperationException("Pen paint factory was not configured.");

    public BColor? SolidColor { get; init; }

    internal bool HasMaterializedPaint => _paint is not null;

    public bool HasSimpleStroke => SolidColor.HasValue && _dashStyle == DashStyle.Solid;

    // RPen.DashStyle is set-only, but the raster path has to read the style back to reduce a
    // dashed stroke to solid runs. Internal so the public pen contract is unchanged.
    internal DashStyle CurrentDashStyle => _dashStyle;

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

    public override DashStyle DashStyle
    {
        set
        {
            _dashStyle = value;
            if (_paint is not null)
                _paintUpdater?.Invoke(_paint, _width, _dashStyle);
        }
    }
}
