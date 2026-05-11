namespace Broiler.HTML.Core.Core.Entities;

/// <summary>
/// Represents a parsed @font-face declaration with its font-family name
/// and source URL.
/// </summary>
public sealed class CssFontFace
{
    /// <summary>CSS font-family name declared in the @font-face rule.</summary>
    public string Family { get; init; } = string.Empty;

    /// <summary>Font source URL (may be relative to the stylesheet/HTML base path).</summary>
    public string Src { get; init; } = string.Empty;
}
