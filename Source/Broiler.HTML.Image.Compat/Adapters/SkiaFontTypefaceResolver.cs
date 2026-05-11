using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using SkiaSharp;

namespace Broiler.HTML.Image.Adapters;

internal sealed class SkiaFontTypefaceResolver : IFontTypefaceResolver
{
    private readonly Dictionary<string, SKTypeface> _loadedTypefaces
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _loadedTypefacePaths
        = new(StringComparer.OrdinalIgnoreCase);

    public string RegisterFontFile(string path, string alias = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        if (!string.IsNullOrWhiteSpace(alias))
        {
            _loadedTypefacePaths[alias] = path;
            _loadedTypefaces.Remove(alias);
            return alias;
        }

        var typeface = SKTypeface.FromFile(path);
        if (typeface == null)
            return null;

        var familyName = typeface.FamilyName;
        _loadedTypefaces[familyName] = typeface;
        _loadedTypefacePaths[familyName] = path;
        return familyName;
    }

    public bool HasDeferredLoadedTypefacePath(string family) =>
        _loadedTypefacePaths.ContainsKey(family);

    public bool HasMaterializedLoadedTypeface(string family) =>
        _loadedTypefaces.ContainsKey(family);

    public object ResolveTypeface(string family, FontStyle style)
    {
        if (_loadedTypefaces.TryGetValue(family, out var loaded))
            return loaded;

        if (_loadedTypefacePaths.TryGetValue(family, out var loadedPath))
        {
            var loadedTypeface = SKTypeface.FromFile(loadedPath);
            if (loadedTypeface != null)
            {
                _loadedTypefaces[family] = loadedTypeface;
                return loadedTypeface;
            }
        }

        var skStyle = ConvertFontStyle(style);
        return SKTypeface.FromFamilyName(family, skStyle) ?? SKTypeface.Default;
    }

    private static SKFontStyle ConvertFontStyle(FontStyle style)
    {
        var weight = (style & FontStyle.Bold) != 0 ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = (style & FontStyle.Italic) != 0 ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        return new SKFontStyle(weight, SKFontStyleWidth.Normal, slant);
    }
}
