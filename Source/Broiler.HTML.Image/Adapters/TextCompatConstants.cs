using System;

namespace Broiler.HTML.Image.Adapters;

internal static class TextCompatConstants
{
    public const double DegreesToRadians = Math.PI / 180.0;
    public const string DeterministicFixtureFontFamily = "Ahem";

    public static bool IsDeterministicFixtureFont(string? familyName) =>
        string.Equals(familyName, DeterministicFixtureFontFamily, StringComparison.OrdinalIgnoreCase);
}
