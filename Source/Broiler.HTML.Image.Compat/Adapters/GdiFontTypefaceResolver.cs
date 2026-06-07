using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using DrawingFontStyle = System.Drawing.FontStyle;

namespace Broiler.HTML.Image.Adapters;

internal sealed class GdiFontTypefaceResolver : IFontTypefaceResolver
{
    private readonly PrivateFontCollection _privateFonts = new();
    private readonly Dictionary<string, FontFamily> _loadedFamilies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _loadedFamilyPaths = new(StringComparer.OrdinalIgnoreCase);

    public string RegisterFontFile(string path, string alias = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        if (!string.IsNullOrWhiteSpace(alias))
        {
            _loadedFamilyPaths[alias] = path;
            _loadedFamilies.Remove(alias);
            return alias;
        }

        var family = LoadFontFile(path);
        if (family is null)
            return null;

        _loadedFamilies[family.Name] = family;
        _loadedFamilyPaths[family.Name] = path;
        return family.Name;
    }

    public bool HasDeferredLoadedTypefacePath(string family) =>
        _loadedFamilyPaths.ContainsKey(family);

    public bool HasMaterializedLoadedTypeface(string family) =>
        _loadedFamilies.ContainsKey(family);

    public object ResolveTypeface(string family, FontStyle style)
    {
        var gdiStyle = ConvertFontStyle(style);

        if (_loadedFamilies.TryGetValue(family, out var loaded))
            return new GdiTypeface(loaded, gdiStyle);

        if (_loadedFamilyPaths.TryGetValue(family, out var loadedPath))
        {
            var loadedFamily = LoadFontFile(loadedPath);
            if (loadedFamily is not null)
            {
                _loadedFamilies[family] = loadedFamily;
                return new GdiTypeface(loadedFamily, gdiStyle);
            }
        }

        try
        {
            return new GdiTypeface(new FontFamily(family), gdiStyle);
        }
        catch (ArgumentException)
        {
            return new GdiTypeface(FontFamily.GenericSansSerif, gdiStyle);
        }
    }

    private FontFamily LoadFontFile(string path)
    {
        int before = _privateFonts.Families.Length;
        _privateFonts.AddFontFile(path);
        return _privateFonts.Families.Length > before
            ? _privateFonts.Families.Last()
            : null;
    }

    private static DrawingFontStyle ConvertFontStyle(FontStyle style)
    {
        var result = DrawingFontStyle.Regular;
        if ((style & FontStyle.Bold) != 0)
            result |= DrawingFontStyle.Bold;
        if ((style & FontStyle.Italic) != 0)
            result |= DrawingFontStyle.Italic;
        return result;
    }
}
