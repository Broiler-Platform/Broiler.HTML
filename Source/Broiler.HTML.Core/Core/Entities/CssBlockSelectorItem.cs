using System;

namespace Broiler.HTML.Core.Core.Entities;

public readonly struct CssBlockSelectorItem
{
    public CssBlockSelectorItem(string @class, bool directParent, bool adjacentSibling = false, string pseudoClass = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(@class);

        Class = @class;
        DirectParent = directParent;
        AdjacentSibling = adjacentSibling;
        PseudoClass = pseudoClass;
    }

    public readonly string Class { get; }
    public readonly bool DirectParent { get; }
    public readonly bool AdjacentSibling { get; }

    /// <summary>
    /// Optional structural pseudo-class (e.g. "first-child", "last-child")
    /// that the matched element must satisfy.  CSS2.1 §5.11.
    /// </summary>
    public readonly string PseudoClass { get; }

    public override readonly string ToString() => Class + (PseudoClass != null ? ":" + PseudoClass : "") + (DirectParent ? " > " : AdjacentSibling ? " + " : string.Empty);
}