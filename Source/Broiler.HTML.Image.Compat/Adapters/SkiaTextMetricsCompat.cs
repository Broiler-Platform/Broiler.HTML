using SkiaSharp;

namespace Broiler.HTML.Image.Adapters;

internal sealed class SkiaTextMetricsCompat : ITextMetricsCompat
{
    public static ITextMetricsCompat Instance { get; } = new SkiaTextMetricsCompat();

    public float MeasureTextWidth(FontAdapter font, string text) =>
        ((SKFont)font.Font).MeasureText(text);

    public void MeasureTextWidth(FontAdapter font, string text, double maxWidth, out int charFit, out double charFitWidth)
    {
        charFit = 0;
        charFitWidth = 0;

        for (int i = 1; i <= text.Length; i++)
        {
            var substring = text.Substring(0, i);
            var width = ((SKFont)font.Font).MeasureText(substring);
            if (width > maxWidth)
                break;

            charFit = i;
            charFitWidth = width;
        }
    }
}
