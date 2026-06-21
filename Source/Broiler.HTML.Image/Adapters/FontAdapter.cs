using System;
using Broiler.HTML.Adapters;
using DrawingFontStyle = Broiler.Graphics.FontStyle;

namespace Broiler.HTML.Image.Adapters;

internal sealed class FontAdapter : RFont
{
    /// <summary>
    /// Ratio to convert typographic points to CSS pixels (96 DPI / 72 DPI).
    /// </summary>
    private const double PtToCssPx = 96.0 / 72.0;
    private readonly double _size;
    private readonly string _family;
    private readonly DrawingFontStyle _style;
    private readonly Func<object>? _compatTypefaceFactory;
    private readonly IFontCompatFactory _fontCompatFactory;
    private double _height = -1;
    private double _underlineOffset = -1;
    private double _whitespaceWidth = -1;
    private object? _typeface;
    private object? _font;
    private object? _renderFont;

    public FontAdapter(
        string family,
        double size,
        DrawingFontStyle style,
        Func<object>? compatTypefaceFactory = null,
        IFontCompatFactory? fontCompatFactory = null)
    {
        _family = family;
        _size = size;
        _style = style;
        _compatTypefaceFactory = compatTypefaceFactory;
        _fontCompatFactory = fontCompatFactory ?? CompatProvider.FontCompatFactory;
    }

    public string Family => _family;

    public DrawingFontStyle Style => _style;

    /// <summary>Layout font (pt-based) – used for metrics and text measurement.</summary>
    public object Font => _font ??= _fontCompatFactory.CreateFont(Typeface, (float)_size);

    /// <summary>Render font (CSS px-based) – used for drawing glyphs at correct size.</summary>
    public object RenderFont => _renderFont ??= _fontCompatFactory.CreateFont(Typeface, (float)(_size * PtToCssPx));

    public object Typeface => _typeface ??= _compatTypefaceFactory?.Invoke()
        ?? throw new InvalidOperationException("Font compatibility typeface factory was not configured.");

    public override double Size => _size;

    public override double Height
    {
        get
        {
            EnsureMetrics();
            return _height;
        }
    }

    public override double UnderlineOffset
    {
        get
        {
            EnsureMetrics();
            return _underlineOffset;
        }
    }

    public override double LeftPadding => Height / 6.0;

    internal bool HasMaterializedLayoutFont => _font is not null;

    internal bool HasMaterializedRenderFont => _renderFont is not null;

    public override double GetWhitespaceWidth(RGraphics graphics)
    {
        if (_whitespaceWidth < 0)
            _whitespaceWidth = graphics.MeasureString(" ", this).Width;

        return _whitespaceWidth;
    }

    private void EnsureMetrics()
    {
        if (_height >= 0 && _underlineOffset >= 0)
            return;

        var compatMetrics = _fontCompatFactory.GetMetrics(Font);
        _height = compatMetrics.Height;
        _underlineOffset = compatMetrics.UnderlineOffset;
    }
}
