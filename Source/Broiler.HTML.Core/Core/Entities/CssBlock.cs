using System;
using System.Collections.Generic;

namespace Broiler.HTML.Core.Core.Entities;

/// <summary>
/// Represents a CSS attribute selector condition like [type="text"] or [hidden].
/// </summary>
public readonly struct CssAttributeCondition(string name, string op, string value)
{

    /// <summary>Attribute name (e.g. "type", "hidden").</summary>
    public string Name { get; } = name;
    /// <summary>Match operator: "=", "~=", "|=", "^=", "$=", "*=", or null for presence-only.</summary>
    public string Op { get; } = op;
    /// <summary>Expected value, or null for presence-only checks like [hidden].</summary>
    public string Value { get; } = value;
}

public sealed class CssBlock
{
    private readonly Dictionary<string, string> _properties;
    private HashSet<string> _importantProperties;

    public CssBlock(string @class, Dictionary<string, string> properties, List<CssBlockSelectorItem> selectors = null, bool hover = false, string pseudoClass = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(@class);
        ArgumentNullException.ThrowIfNull(properties);

        Class = @class;
        Selectors = selectors;
        _properties = properties;
        Hover = hover;
        PseudoClass = pseudoClass;
    }

    /// <summary>
    /// Whether this block originated from the user-agent default stylesheet.
    /// CSS2.1 §6.4.1: Author declarations override UA declarations
    /// regardless of specificity.
    /// </summary>
    public bool IsUserAgent { get; internal set; }

    public string Class { get; }
    public List<CssBlockSelectorItem> Selectors { get; }
    public IDictionary<string, string> Properties => _properties;
    public bool Hover { get; }

    /// <summary>
    /// Optional structural pseudo-class on the terminal selector
    /// (e.g. "first-child" for <c>h1:first-child</c>).  CSS2.1 §5.11.
    /// </summary>
    public string PseudoClass { get; internal set; }

    /// <summary>Attribute selector conditions extracted from the CSS selector.</summary>
    public List<CssAttributeCondition> AttributeConditions { get; internal set; }

    /// <summary>
    /// Encoded CSS specificity for this block using the usual
    /// <c>(a,b,c) → a*1_000_000 + b*1_000 + c</c> ordering.
    /// </summary>
    public int Specificity { get; internal set; }

    /// <summary>
    /// Source-order index assigned while parsing the stylesheet so equal-
    /// specificity rules still cascade in declaration order.
    /// </summary>
    public int SourceOrder { get; internal set; }

    /// <summary>
    /// Property names in this block that were declared with <c>!important</c>.
    /// CSS2.1 §6.4.2: Important declarations override normal declarations
    /// regardless of specificity.
    /// </summary>
    public IReadOnlySet<string> ImportantProperties => _importantProperties ?? (IReadOnlySet<string>)_emptySet;
    private static readonly HashSet<string> _emptySet = [];

    /// <summary>
    /// Marks the given property name as <c>!important</c>.
    /// </summary>
    public void MarkImportant(string propertyName)
    {
        _importantProperties ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _importantProperties.Add(propertyName);
    }

    public void Merge(CssBlock other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach (var prop in other._properties.Keys)
            _properties[prop] = other._properties[prop];

        if (other._importantProperties != null)
        {
            _importantProperties ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in other._importantProperties)
                _importantProperties.Add(prop);
        }
    }

    public CssBlock Clone()
    {
        var clone = new CssBlock(Class, new Dictionary<string, string>(_properties), Selectors != null ? [.. Selectors] : null, Hover, PseudoClass)
        {
            IsUserAgent = IsUserAgent,
            Specificity = Specificity,
            SourceOrder = SourceOrder
        };
        if (_importantProperties != null)
            clone._importantProperties = new HashSet<string>(_importantProperties, StringComparer.OrdinalIgnoreCase);
        if (AttributeConditions != null)
            clone.AttributeConditions = [.. AttributeConditions];
        return clone;
    }

    public bool Equals(CssBlock other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (!Equals(other.Class, Class))
            return false;

        if (!Equals(other._properties.Count, _properties.Count))
            return false;

        foreach (var property in _properties)
        {
            if (!other._properties.TryGetValue(property.Key, out string value))
                return false;

            if (!Equals(value, property.Value))
                return false;
        }

        if (!EqualsSelector(other))
            return false;

        return true;
    }

    public bool EqualsSelector(CssBlock other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (other.Hover != Hover)
            return false;

        // CSS2.1 §5.11: Blocks with different structural pseudo-classes
        // (e.g. :lang(en) vs :lang(fr), or :first-child vs none) must
        // not be merged; they have different matching conditions.
        if (!string.Equals(other.PseudoClass, PseudoClass, StringComparison.OrdinalIgnoreCase))
            return false;

        if (other.Selectors == null && Selectors != null)
            return false;

        if (other.Selectors != null && Selectors == null)
            return false;

        if (other.Selectors != null && Selectors != null)
        {
            if (!Equals(other.Selectors.Count, Selectors.Count))
                return false;

            for (int i = 0; i < Selectors.Count; i++)
            {
                if (!Equals(other.Selectors[i].Class, Selectors[i].Class))
                    return false;

                if (!Equals(other.Selectors[i].DirectParent, Selectors[i].DirectParent))
                    return false;

                if (!Equals(other.Selectors[i].AdjacentSibling, Selectors[i].AdjacentSibling))
                    return false;
            }
        }

        // CSS2.1 §5.8.1: Attribute selectors change specificity and must
        // not be merged with blocks that have different (or no) attribute
        // conditions.  For example, input[type="hidden"] must remain
        // separate from a plain input rule.
        if (!EqualsAttributeConditions(other.AttributeConditions, AttributeConditions))
            return false;

        return true;
    }

    private static bool EqualsAttributeConditions(List<CssAttributeCondition> a, List<CssAttributeCondition> b)
    {
        bool aEmpty = a == null || a.Count == 0;
        bool bEmpty = b == null || b.Count == 0;

        if (aEmpty && bEmpty)
            return true;
        if (aEmpty != bEmpty)
            return false;

        if (a.Count != b.Count)
            return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Name, b[i].Name, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(a[i].Op, b[i].Op, StringComparison.Ordinal))
                return false;
            if (!string.Equals(a[i].Value, b[i].Value, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public override bool Equals(object obj)
    {
        if (obj is null)
            return false;

        if (ReferenceEquals(this, obj))
            return true;

        if (obj.GetType() != typeof(CssBlock))
            return false;

        return Equals((CssBlock)obj);
    }

    public override int GetHashCode() => HashCode.Combine(Class.GetHashCode(), _properties.GetHashCode());

    public override string ToString()
    {
        var str = Class + " { ";

        foreach (var property in _properties)
            str += $"{property.Key}={property.Value}; ";

        return str + " }";
    }
}
