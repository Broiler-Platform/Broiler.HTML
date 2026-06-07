using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.IO;
using System.Linq;

namespace Broiler.HTML.Image.Adapters;

/// <summary>
/// Tracks the font families available to the image backend: installed system
/// fonts plus any font files registered at runtime.
/// </summary>
internal static class BroilerFontRegistry
{
    private static readonly object Sync = new();
    private static readonly PrivateFontCollection LoadedFonts = new();
    private static readonly HashSet<string> LoadedFamilies = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> GetSystemFontFamilies()
    {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var installed = new InstalledFontCollection())
        {
            foreach (var family in installed.Families)
                families.Add(family.Name);
        }

        lock (Sync)
        {
            foreach (var name in LoadedFamilies)
                families.Add(name);
        }

        return families;
    }

    public static void RegisterFontFile(string path, string? alias)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        lock (Sync)
        {
            int before = LoadedFonts.Families.Length;
            LoadedFonts.AddFontFile(path);
            var added = LoadedFonts.Families.Length > before
                ? LoadedFonts.Families.Last()
                : null;

            if (added is not null)
                LoadedFamilies.Add(added.Name);

            if (!string.IsNullOrWhiteSpace(alias))
                LoadedFamilies.Add(alias);
        }
    }
}
