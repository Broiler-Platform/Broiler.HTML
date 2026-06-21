using Broiler.HTML.Core;
using Broiler.HTML.CSS.Core.Parse;

namespace Broiler.HTML.CSS.Core;


internal static class CssDataParser
{
    public static CssData Parse(IColorResolver colorResolver, string stylesheet, CssData defaultCssData = null)
    {
        CssParser parser = new(colorResolver);
        return parser.ParseStyleSheet(stylesheet, defaultCssData);
    }
}
