using Broiler.HTML.Adapters;
using System.Windows.Media;

namespace Broiler.HTML.WPF.Adapters;

internal sealed class FontAdapter : RFont
{
    private readonly double _size;
    private readonly double _underlineOffset = -1;
    private readonly double _height = -1;
    private double _whitespaceWidth = -1;

    public FontAdapter(Typeface font, double size)
    {
        Font = font;
        _size = size;
        _height = 96d / 72d * _size * Font.FontFamily.LineSpacing;
        _underlineOffset = 96d / 72d * _size * (Font.FontFamily.LineSpacing + font.UnderlinePosition);

        if (font.TryGetGlyphTypeface(out GlyphTypeface typeface))
        {
            GlyphTypeface = typeface;
        }
        else
        {
            foreach (var sysTypeface in Fonts.SystemTypefaces)
            {
                if (sysTypeface.TryGetGlyphTypeface(out typeface))
                    break;
            }
        }
    }

    public Typeface Font { get; }

    public GlyphTypeface GlyphTypeface { get; }

    public override double Size => _size;

    public override double UnderlineOffset => _underlineOffset;

    public override double Height => _height;

    public override double LeftPadding => _height / 6f;

    public override double GetWhitespaceWidth(RGraphics graphics)
    {
        if (_whitespaceWidth < 0)
            _whitespaceWidth = graphics.MeasureString(" ", this).Width;

        return _whitespaceWidth;
    }
}