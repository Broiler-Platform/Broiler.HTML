namespace Broiler.HTML.Image.Adapters;

internal interface IFontCompatFactory
{
    object CreateFont(object typeface, float size);

    FontCompatMetrics GetMetrics(object font);
}

internal readonly record struct FontCompatMetrics(double Height, double UnderlineOffset);
