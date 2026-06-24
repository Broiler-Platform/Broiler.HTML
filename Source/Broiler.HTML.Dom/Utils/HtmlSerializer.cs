using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Broiler.HTML.Dom.Utils;

/// <summary>
/// Shared HTML serialization helpers used by both the Broiler.HTML rendering
/// pipeline and the DomBridge JavaScript execution bridge.
/// </summary>
public static class HtmlSerializer
{
    /// <summary>
    /// HTML-encodes a string value for safe inclusion in attributes or text
    /// content by replacing <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, and
    /// <c>&quot;</c> with their entity equivalents.
    /// </summary>
    public static string HtmlEncode(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="property"/> is a CSS shorthand
    /// that, if emitted after its longhands, would reset those longhands to
    /// initial values (e.g. <c>margin</c> resets <c>margin-left</c>).
    /// </summary>
    public static bool IsShorthandProperty(string property)
    {
        return property switch
        {
            "margin" or "padding" or "border" or "background"
                or "font" or "list-style" or "outline" => true,
            _ => false,
        };
    }

    /// <summary>
    /// Well-known void elements that have no closing tag per the HTML specification.
    /// </summary>
    public static readonly HashSet<string> VoidTags = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr"
    };
}
