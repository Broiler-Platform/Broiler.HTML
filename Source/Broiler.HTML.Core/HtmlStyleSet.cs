using System;
using System.Linq;
using Broiler.CSS;

namespace Broiler.HTML.Core;

/// <summary>
/// Immutable renderer stylesheet input with cascade origin preserved.
/// </summary>
/// <remarks>
/// A bare <see cref="Broiler.CSS.CssStyleSheet"/> cannot distinguish user-agent
/// defaults from author rules. This type is the supported renderer-facing CSS API.
/// </remarks>
public sealed class HtmlStyleSet
{
    private static readonly CssStyleSheet EmptySheet = new([], []);
    private static readonly CssStyleSheet DefaultUserAgentSheet = new CssParser().ParseStyleSheet(CssDefaults.DefaultStyleSheet);

    private readonly Lazy<CssStyleSheet> _combinedStyleSheet;

    public HtmlStyleSet(CssStyleSheet? authorStyleSheet = null, CssStyleSheet? userAgentStyleSheet = null)
    {
        AuthorStyleSheet = authorStyleSheet ?? EmptySheet;
        UserAgentStyleSheet = userAgentStyleSheet ?? EmptySheet;
        _combinedStyleSheet = new Lazy<CssStyleSheet>(() => CombineSheets(UserAgentStyleSheet, AuthorStyleSheet));
    }

    /// <summary>Gets an empty style set.</summary>
    public static HtmlStyleSet Empty { get; } = new();

    /// <summary>Gets the renderer's default user-agent stylesheet.</summary>
    public static HtmlStyleSet Default { get; } = new(userAgentStyleSheet: DefaultUserAgentSheet);

    /// <summary>Gets author-origin rules.</summary>
    public CssStyleSheet AuthorStyleSheet { get; }

    /// <summary>Gets user-agent-origin rules.</summary>
    public CssStyleSheet UserAgentStyleSheet { get; }

    /// <summary>
    /// Gets a combined model view for inspection and serialization. Runtime cascade
    /// must use the origin-specific properties above.
    /// </summary>
    public CssStyleSheet StyleSheet => _combinedStyleSheet.Value;

    /// <summary>Parses author CSS and optionally includes renderer defaults.</summary>
    public static HtmlStyleSet Parse(string? stylesheet, bool includeDefaults = true)
    {
        var author = new CssParser().ParseStyleSheet(stylesheet);
        return new HtmlStyleSet(author, includeDefaults ? DefaultUserAgentSheet : EmptySheet);
    }

    /// <summary>Returns a style set with additional author-origin rules appended.</summary>
    public HtmlStyleSet AppendAuthorStyleSheet(CssStyleSheet styleSheet)
    {
        ArgumentNullException.ThrowIfNull(styleSheet);
        return new HtmlStyleSet(CombineSheets(AuthorStyleSheet, styleSheet), UserAgentStyleSheet);
    }

    private static CssStyleSheet CombineSheets(CssStyleSheet first, CssStyleSheet second) =>
        new(first.Rules.Concat(second.Rules), first.Diagnostics.Concat(second.Diagnostics));
}
