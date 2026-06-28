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
    private static readonly Broiler.CSS.CssStyleSheet EmptySheet = new([], []);
    private static readonly Broiler.CSS.CssStyleSheet DefaultUserAgentSheet =
        new Broiler.CSS.CssParser().ParseStyleSheet(CssDefaults.DefaultStyleSheet);

    private readonly Lazy<Broiler.CSS.CssStyleSheet> _combinedStyleSheet;

    public HtmlStyleSet(
        Broiler.CSS.CssStyleSheet? authorStyleSheet = null,
        Broiler.CSS.CssStyleSheet? userAgentStyleSheet = null)
    {
        AuthorStyleSheet = authorStyleSheet ?? EmptySheet;
        UserAgentStyleSheet = userAgentStyleSheet ?? EmptySheet;
        _combinedStyleSheet = new Lazy<Broiler.CSS.CssStyleSheet>(
            () => CombineSheets(UserAgentStyleSheet, AuthorStyleSheet));
    }

    /// <summary>Gets an empty style set.</summary>
    public static HtmlStyleSet Empty { get; } = new();

    /// <summary>Gets the renderer's default user-agent stylesheet.</summary>
    public static HtmlStyleSet Default { get; } = new(userAgentStyleSheet: DefaultUserAgentSheet);

    /// <summary>Gets author-origin rules.</summary>
    public Broiler.CSS.CssStyleSheet AuthorStyleSheet { get; }

    /// <summary>Gets user-agent-origin rules.</summary>
    public Broiler.CSS.CssStyleSheet UserAgentStyleSheet { get; }

    /// <summary>
    /// Gets a combined model view for inspection and serialization. Runtime cascade
    /// must use the origin-specific properties above.
    /// </summary>
    public Broiler.CSS.CssStyleSheet StyleSheet => _combinedStyleSheet.Value;

    /// <summary>Parses author CSS and optionally includes renderer defaults.</summary>
    public static HtmlStyleSet Parse(string? stylesheet, bool includeDefaults = true)
    {
        var author = new Broiler.CSS.CssParser().ParseStyleSheet(stylesheet);
        return new HtmlStyleSet(author, includeDefaults ? DefaultUserAgentSheet : EmptySheet);
    }

    /// <summary>Returns a style set with additional author-origin rules appended.</summary>
    public HtmlStyleSet AppendAuthorStyleSheet(Broiler.CSS.CssStyleSheet styleSheet)
    {
        ArgumentNullException.ThrowIfNull(styleSheet);
        return new HtmlStyleSet(
            CombineSheets(AuthorStyleSheet, styleSheet),
            UserAgentStyleSheet);
    }

    /// <summary>Returns a style set containing both inputs with origins preserved.</summary>
    public HtmlStyleSet Combine(HtmlStyleSet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new HtmlStyleSet(
            CombineSheets(AuthorStyleSheet, other.AuthorStyleSheet),
            CombineSheets(UserAgentStyleSheet, other.UserAgentStyleSheet));
    }

    private static Broiler.CSS.CssStyleSheet CombineSheets(
        Broiler.CSS.CssStyleSheet first,
        Broiler.CSS.CssStyleSheet second) =>
        new(
            first.Rules.Concat(second.Rules),
            first.Diagnostics.Concat(second.Diagnostics));
}
