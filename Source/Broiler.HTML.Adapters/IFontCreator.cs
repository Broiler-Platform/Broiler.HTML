namespace Broiler.HTML.Adapters;

internal interface IFontCreator
{
    RFont CreateFont(string family, double size, FontStyle style);
    RFont CreateFont(RFontFamily family, double size, FontStyle style);
}
