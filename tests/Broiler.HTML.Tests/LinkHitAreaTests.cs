using System.Drawing;
using Broiler.HTML.Image;

namespace Broiler.HTML.Tests;

/// <summary>
/// A link is clicked anywhere in its box: on its text, and in the rest of the area a block-level
/// link takes up.
/// </summary>
/// <remarks>
/// The hit test looked only at a link's line rectangles, which a link laid out as a block does not
/// have: it answered on its words and nowhere else, so a navigation item whose <c>display: block</c>
/// link fills its padding was dead everywhere but on its text.
/// </remarks>
public sealed class LinkHitAreaTests
{
    private const string Target = "https://example.test/target";

    [Theory]
    // On the text, which worked before.
    [InlineData("display: block; height: 100px", 5, 5, true)]
    // In a block link's box, away from its text.
    [InlineData("display: block; height: 100px", 400, 60, true)]
    [InlineData("display: flex; height: 100px", 400, 60, true)]
    // Below the link: nothing.
    [InlineData("display: block; height: 100px", 400, 150, false)]
    // Beside an inline link's text, on the same line: nothing, as in a browser.
    [InlineData("display: inline", 400, 5, false)]
    public void A_Link_Is_Clicked_Where_Its_Box_Is(string style, float x, float y, bool clicked)
    {
        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            MaxSize = new SizeF(800, 600),
        };
        container.SetHtmlWithStyleSet(
            $"<!DOCTYPE html><html><body style=\"margin: 0\"><a href=\"{Target}\" style=\"{style}\">link</a></body></html>",
            null,
            "https://example.test/");
        container.PerformLayout();

        string? link = null;
        container.LinkClicked += (_, e) =>
        {
            link = e.Link;
            e.Handled = true;
        };
        container.HandleMouseDown(new PointF(x, y));
        container.HandleMouseUp(new PointF(x, y));

        Assert.Equal(clicked ? Target : null, link);
    }
}
