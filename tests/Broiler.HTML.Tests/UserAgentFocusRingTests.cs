using Broiler.CSS;
using Broiler.HTML.Core;

namespace Broiler.HTML.Tests;

/// <summary>
/// The renderer's own focus ring is drawn where Chromium draws its: on an element that matches
/// <c>:focus-visible</c>, not on every element that has focus.
/// </summary>
/// <remarks>
/// <para>
/// The user-agent sheet is CSS 2.1's sample one, whose <c>:focus { outline: thin dotted invert }</c>
/// rings every focused element. It never mattered while nothing matched <c>:focus</c>; a page that a
/// scripting host serializes while someone uses it carries the focused element's state in its markup
/// now, and a button the mouse pressed would have been ringed where Chromium draws nothing. Chromium,
/// like the HTML Standard's rendering section, rings <c>:focus-visible</c>: a field, and whatever the
/// keyboard moved focus to.
/// </para>
/// </remarks>
public sealed class UserAgentFocusRingTests
{
    [Fact]
    public void The_Focus_Ring_Rule_Is_For_Focus_Visible_Alone()
    {
        var outlines = HtmlStyleSet.Default.UserAgentStyleSheet.Rules
            .OfType<CssStyleRule>()
            .Where(static rule => rule.Declarations.GetPropertyValue("outline") is not null)
            .SelectMany(static rule => rule.Selectors.Selectors)
            .Select(static selector => selector.Text)
            .ToList();

        Assert.Equal([":focus-visible"], outlines);
    }
}
