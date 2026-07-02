using System.Collections.Generic;

namespace Broiler.HTML.Utils;

internal static class HtmlUtils
{
    private static readonly List<string> _list = new(
        [
            "area", "base", "basefont", "br", "col",
            "embed", "frame", "hr", "img", "input",
            "isindex", "link", "meta", "param",
            "source", "track", "wbr"
        ]
        );

    public static bool IsSingleTag(string tagName) => _list.Contains(tagName);
}