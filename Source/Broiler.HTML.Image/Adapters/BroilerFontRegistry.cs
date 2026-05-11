using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using SixLabors.Fonts;
using DrawingFontStyle = System.Drawing.FontStyle;
using SixLaborsFont = SixLabors.Fonts.Font;
using SixLaborsFontFamily = SixLabors.Fonts.FontFamily;

namespace Broiler.HTML.Image.Adapters;

internal static class BroilerFontRegistry
{
    private static readonly object Sync = new();
    private static readonly FontCollection LoadedFonts = new();
    private static readonly Dictionary<string, SixLaborsFontFamily> LoadedFamilies = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<IReadOnlyCollection<string>> SystemFamilies = new(
        static () => new HashSet<string>(
            SixLabors.Fonts.SystemFonts.Families.Select(static family => family.Name),
            StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyCollection<string> GetSystemFontFamilies() => SystemFamilies.Value;

    public static void RegisterFontFile(string path, string? alias)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        lock (Sync)
        {
            var family = LoadedFonts.Add(path);
            LoadedFamilies[family.Name] = family;
            if (!string.IsNullOrWhiteSpace(alias))
                LoadedFamilies[alias] = family;
        }
    }

    public static bool TryCreateFont(string familyName, float size, DrawingFontStyle style, out SixLaborsFont font)
    {
        font = default;
        if (string.IsNullOrWhiteSpace(familyName) || size <= 0)
            return false;

        if (TryGetFamily(familyName, out var family))
        {
            font = family.CreateFont(size, ConvertStyle(style));
            return true;
        }

        return false;
    }

    private static bool TryGetFamily(string familyName, out SixLaborsFontFamily family)
    {
        lock (Sync)
        {
            if (LoadedFamilies.TryGetValue(familyName, out family))
                return true;
        }

        return SixLabors.Fonts.SystemFonts.TryGet(familyName, out family);
    }

    private static SixLabors.Fonts.FontStyle ConvertStyle(DrawingFontStyle style) => style switch
    {
        _ when (style & DrawingFontStyle.Bold) != 0 && (style & DrawingFontStyle.Italic) != 0 => SixLabors.Fonts.FontStyle.BoldItalic,
        _ when (style & DrawingFontStyle.Bold) != 0 => SixLabors.Fonts.FontStyle.Bold,
        _ when (style & DrawingFontStyle.Italic) != 0 => SixLabors.Fonts.FontStyle.Italic,
        _ => SixLabors.Fonts.FontStyle.Regular,
    };
}
