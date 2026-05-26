using SkiaSharp;

namespace Broiler.HTML.Image.Adapters;

internal sealed class SkiaFontCompatFactory : IFontCompatFactory
{
    public static IFontCompatFactory Instance { get; } = new SkiaFontCompatFactory();

    public object CreateFont(object typeface, float size) =>
        new SKFont(SkiaCompatObjects.Typeface(typeface), size)
        {
            Edging = SKFontEdging.Alias,
            Hinting = SKFontHinting.Slight,
            Subpixel = false,
        };

    public FontCompatMetrics GetMetrics(object font)
    {
        var skFont = SkiaCompatObjects.Font(font);
        var skiaMetrics = skFont.Metrics;
        double height = skiaMetrics.Descent - skiaMetrics.Ascent;
        double underlineOffset = -skiaMetrics.Ascent
            + skiaMetrics.UnderlinePosition.GetValueOrDefault(skiaMetrics.Descent - skiaMetrics.Ascent * 0.87f);
        return new FontCompatMetrics(height, underlineOffset);
    }
}
