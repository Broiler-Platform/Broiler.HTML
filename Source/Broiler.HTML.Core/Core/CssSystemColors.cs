using System.Drawing;

namespace Broiler.HTML.Core.Core;

internal static class CssSystemColors
{
    public static bool TryResolve(string colorName, out Color color)
    {
        if (string.IsNullOrWhiteSpace(colorName))
        {
            color = Color.Empty;
            return false;
        }

        switch (colorName.Trim().ToLowerInvariant())
        {
            case "field":
                color = Color.White;
                return true;
            case "fieldtext":
                color = Color.Black;
                return true;
            default:
                color = Color.Empty;
                return false;
        }
    }
}
