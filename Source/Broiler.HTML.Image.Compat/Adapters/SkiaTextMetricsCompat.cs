using SkiaSharp;

namespace Broiler.HTML.Image.Adapters;

internal sealed class SkiaTextMetricsCompat : ITextMetricsCompat
{
    /// <summary>
    /// Ratio to convert typographic points to CSS pixels (96 DPI / 72 DPI).
    /// The layout font is created at point size but CSS layout uses pixel
    /// units, so every measurement must be scaled by this factor.
    /// </summary>
    private const float PtToCssPx = 96f / 72f;

    public static ITextMetricsCompat Instance { get; } = new SkiaTextMetricsCompat();

    public float MeasureTextWidth(FontAdapter font, string text) =>
        ((SKFont)font.Font).MeasureText(text) * PtToCssPx;

    public void MeasureTextWidth(FontAdapter font, string text, double maxWidth, out int charFit, out double charFitWidth)
    {
        charFit = 0;
        charFitWidth = 0;

        for (int i = 1; i <= text.Length; i++)
        {
            var substring = text.Substring(0, i);
            var width = ((SKFont)font.Font).MeasureText(substring) * PtToCssPx;
            if (width > maxWidth)
                break;

            charFit = i;
            charFitWidth = width;
        }
    }
}
