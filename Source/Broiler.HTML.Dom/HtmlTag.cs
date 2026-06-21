using System;
using System.Collections.Generic;

namespace Broiler.HTML.Dom;

internal sealed class HtmlTag
{
    public HtmlTag(string name, bool isSingle, Dictionary<string, string> attributes = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        Name = name;
        IsSingle = isSingle;
        Attributes = attributes;
    }

    public string Name { get; }
    public Dictionary<string, string> Attributes { get; }
    public bool IsSingle { get; }
    public bool HasAttributes() => Attributes != null && Attributes.Count > 0;
    public bool HasAttribute(string attribute) => Attributes != null && Attributes.ContainsKey(attribute);
    public string TryGetAttribute(string attribute, string defaultValue = null) => Attributes != null && Attributes.TryGetValue(attribute, out string value) ? value : defaultValue;

    public override string ToString() => $"<{Name}>";
}