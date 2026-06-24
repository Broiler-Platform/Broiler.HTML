using System.Drawing;

namespace Broiler.HTML.Core;

/// <summary>
/// Interface for resolving color names to color values.
/// Breaks the circular dependency between CSS parsing and the adapter layer.
/// </summary>
internal interface IColorResolver
{
    /// <summary>
    /// Resolves a color name to its <see cref="Color"/> value.
    /// </summary>
    Color GetColor(string colorName);

    /// <summary>
    /// Checks whether a font family is available.
    /// </summary>
    bool IsFontExists(string family);
}
