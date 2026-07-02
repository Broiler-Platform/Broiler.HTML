using System;
using System.Linq;

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
    private static readonly CSS.CssStyleSheet EmptySheet = new([], []);
    private static readonly CSS.CssStyleSheet DefaultUserAgentSheet =
        new CSS.CssParser().ParseStyleSheet(CssDefaults.DefaultStyleSheet);

    private readonly Lazy<CSS.CssStyleSheet> _combinedStyleSheet;

    public HtmlStyleSet(
        CSS.CssStyleSheet? authorStyleSheet = null,
        CSS.CssStyleSheet? userAgentStyleSheet = null)
    {
        AuthorStyleSheet = authorStyleSheet ?? EmptySheet;
        UserAgentStyleSheet = userAgentStyleSheet ?? EmptySheet;
        _combinedStyleSheet = new Lazy<CSS.CssStyleSheet>(
            () => CombineSheets(UserAgentStyleSheet, AuthorStyleSheet));
    }

    /// <summary>Gets an empty style set.</summary>
    public static HtmlStyleSet Empty { get; } = new();

    /// <summary>Gets the renderer's default user-agent stylesheet.</summary>
    public static HtmlStyleSet Default { get; } = new(userAgentStyleSheet: DefaultUserAgentSheet);

    /// <summary>Gets author-origin rules.</summary>
    public CSS.CssStyleSheet AuthorStyleSheet { get; }

    /// <summary>Gets user-agent-origin rules.</summary>
    public CSS.CssStyleSheet UserAgentStyleSheet { get; }

    /// <summary>
    /// Gets a combined model view for inspection and serialization. Runtime cascade
    /// must use the origin-specific properties above.
    /// </summary>
    public CSS.CssStyleSheet StyleSheet => _combinedStyleSheet.Value;

    /// <summary>Parses author CSS and optionally includes renderer defaults.</summary>
    public static HtmlStyleSet Parse(string? stylesheet, bool includeDefaults = true)
    {
        var author = new CSS.CssParser().ParseStyleSheet(stylesheet);
        return new HtmlStyleSet(author, includeDefaults ? DefaultUserAgentSheet : EmptySheet);
    }

    /// <summary>Returns a style set with additional author-origin rules appended.</summary>
    public HtmlStyleSet AppendAuthorStyleSheet(CSS.CssStyleSheet styleSheet)
    {
        ArgumentNullException.ThrowIfNull(styleSheet);
        return new HtmlStyleSet(
            CombineSheets(AuthorStyleSheet, styleSheet),
            UserAgentStyleSheet);
    }

    private static CSS.CssStyleSheet CombineSheets(
        CSS.CssStyleSheet first,
        CSS.CssStyleSheet second) =>
        new(
            first.Rules.Concat(second.Rules),
            first.Diagnostics.Concat(second.Diagnostics));
}
