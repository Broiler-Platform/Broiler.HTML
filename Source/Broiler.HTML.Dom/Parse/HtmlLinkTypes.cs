using System;

namespace Broiler.HTML.Dom.Parse;

/// <summary>
/// HTML §4.6.1: a <c>rel</c> attribute is a set of space-separated tokens, and HTML's "space" is
/// ASCII whitespace (§2.3.8) — tab, line feed, form feed and carriage return as well as the space
/// character. Keywords match ASCII case-insensitively.
/// </summary>
/// <remarks>
/// The stylesheet collection and the resource-log dump each split on the space character only, so a
/// <c>rel</c> that put a line break or tab between its tokens (<c>rel="preload&#10;stylesheet"</c>)
/// loaded no sheet.
/// </remarks>
internal static class HtmlLinkTypes
{
    private static readonly char[] AsciiWhitespace = ['\t', '\n', '\f', '\r', ' '];

    /// <summary>Whether <paramref name="rel"/> holds <paramref name="keyword"/> as one of its tokens.</summary>
    public static bool Contains(string? rel, string keyword)
    {
        if (string.IsNullOrEmpty(rel))
            return false;

        foreach (var token in rel.Split(AsciiWhitespace, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
