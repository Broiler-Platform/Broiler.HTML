namespace Broiler.HTML.Image.Adapters;

internal interface ITextMetricsCompat
{
    float MeasureTextWidth(FontAdapter font, string text);

    void MeasureTextWidth(FontAdapter font, string text, double maxWidth, out int charFit, out double charFitWidth);
}
