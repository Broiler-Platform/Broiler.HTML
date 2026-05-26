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
                color = Color.FromArgb(255, 255, 255, 255);
                return true;
            case "fieldtext":
                color = Color.FromArgb(255, 0, 0, 0);
                return true;
            default:
                color = Color.Empty;
                return false;
        }
    }
}
