using System;
using Broiler.Graphics;
using Broiler.Graphics.Adapters;
using Broiler.Graphics.Text;

namespace Broiler.HTML.Image.Adapters;

internal sealed class FontAdapter(
    string family,
    double size,
    FontStyle style,
    Func<object>? compatTypefaceFactory = null,
    IFontCompatFactory? fontCompatFactory = null,
    Func<object, BFontFace?>? registeredFaceResolver = null) : BFont
{
    private readonly IFontCompatFactory _fontCompatFactory = fontCompatFactory ?? CompatProvider.FontCompatFactory;
    private double _height = -1;
    private double _underlineOffset = -1;
    private double _whitespaceWidth = -1;
    private object? _typeface;
    private object? _font;
    private BFontFace? _face;
    private bool _faceResolved;

    /// <summary>Layout font (pt-based) – used for metrics and text measurement.</summary>
    public object Font => _font ??= _fontCompatFactory.CreateFont(Typeface, (float)size);

    public object Typeface => _typeface ??= compatTypefaceFactory?.Invoke()
        ?? throw new InvalidOperationException("Font compatibility typeface factory was not configured.");

    public override double Size => size;

    /// <summary>
    /// The family this font resolved to. <c>FontsHandler.GetCachedFont</c> resolves the CSS
    /// <c>font-family</c> list before constructing the adapter, so this is one installed family
    /// name — the face every width on this font was measured with.
    /// </summary>
    public override string Family => family;

    public override FontStyle Style => style;

    /// <summary>
    /// The program of <see cref="Typeface"/> when it is a font registered at runtime (an
    /// <c>@font-face</c> web font), so a render list can carry the face every width on this font was
    /// measured with to a backend that would otherwise look <see cref="Family"/> up among installed
    /// fonts and draw another. Derived from the typeface itself rather than from the family, so the
    /// two cannot disagree if the family is registered again.
    /// </summary>
    public override BFontFace? Face
    {
        get
        {
            if (!_faceResolved)
            {
                _face = registeredFaceResolver?.Invoke(Typeface);
                _faceResolved = true;
            }

            return _face;
        }
    }

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

    public override double GetWhitespaceWidth(BGraphics graphics)
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

        // A registered face (an @font-face web font) has its content area from its own ascent to
        // its descent, as CSS 2 §10.8.1 gives every font, rather than the 1.16em every other font
        // shares here. Layout hangs the glyphs from the top of that area, half the leading above
        // it, so with the face's own extent they land where a browser draws them: Ahem's ascent and
        // descent add up to one em, and in a 1.16em area its full-em "X" rose 2px above the 20px
        // box it is meant to fill (Acid3's map::after). The underline keeps its share of the height.
        if (Face is { } face && _height > 0)
        {
            TrueTypeFont program = face.Font;
            double extent = (program.Ascender - program.Descender) * BFontStyle.PointsToPixels(size) / program.UnitsPerEm;
            if (extent > 0)
            {
                _underlineOffset *= extent / _height;
                _height = extent;
            }
        }
    }
}
