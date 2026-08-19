using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Broiler.HTML.Image.Adapters;

/// <summary>
/// Tracks the font families available to the image backend. The OS-dependent
/// GDI+ system-font enumeration (<c>InstalledFontCollection</c>/
/// <c>PrivateFontCollection</c>) has been removed; this now records only the
/// fonts registered at runtime via <see cref="RegisterFontFile"/>. A managed or
/// native font backend can populate the same registry once available.
/// </summary>
internal static class BroilerFontRegistry
{
    private static readonly Lock Sync = new();
    private static readonly HashSet<string> LoadedFamilies = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> GetSystemFontFamilies()
    {
        // The installed families, which this registry could not name for itself: reading a font
        // file's `name` table is what tells you the family CSS asks for it by, and that index
        // lives in Broiler.Layout (Broiler.Layout.Text.SystemFontIndex). Without them the
        // adapter advertised no family at all, so every font-family — including `serif` and
        // `monospace` — resolved to the one fallback face the text backend carries.
        var families = new HashSet<string>(
            Broiler.Layout.Text.SystemFontIndex.Families,
            StringComparer.OrdinalIgnoreCase);

        lock (Sync)
        {
            families.UnionWith(LoadedFamilies);
        }

        return [.. families];
    }

    public static void RegisterFontFile(string path, string? alias)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        lock (Sync)
        {
            // Without an OS/managed font parser we cannot read the embedded
            // family name, so fall back to the supplied alias or the file name.
            var family = !string.IsNullOrWhiteSpace(alias)
                ? alias
                : Path.GetFileNameWithoutExtension(path);

            if (!string.IsNullOrWhiteSpace(family))
                LoadedFamilies.Add(family);

            if (!string.IsNullOrWhiteSpace(alias))
                LoadedFamilies.Add(alias);
        }
    }
}
