using System;

namespace Broiler.HTML.Core;

/// <summary>
/// One-release compatibility wrapper for <see cref="HtmlStyleSet"/>.
/// </summary>
[Obsolete("Use HtmlStyleSet and Broiler.CSS.CssStyleSheet. CssData will be removed after the compatibility window.")]
public sealed class CssData
{
    internal CssData(HtmlStyleSet? styleSet = null) => StyleSet = styleSet ?? HtmlStyleSet.Empty;

    /// <summary>Gets the origin-aware renderer style set.</summary>
    public HtmlStyleSet StyleSet { get; private set; }

    /// <summary>Gets the combined shared stylesheet model.</summary>
    public Broiler.CSS.CssStyleSheet StyleSheet => StyleSet.StyleSheet;

    /// <summary>Combines another compatibility wrapper while preserving origins.</summary>
    public void Combine(CssData other)
    {
        ArgumentNullException.ThrowIfNull(other);
        StyleSet = StyleSet.Combine(other.StyleSet);
    }

    /// <summary>Clones this compatibility wrapper.</summary>
    public CssData Clone() => new(StyleSet);
}
