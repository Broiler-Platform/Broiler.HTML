using System.Drawing;
using Broiler.HTML.Image;

namespace Broiler.HTML.Tests;

/// <summary>
/// A <c>&lt;link&gt;</c>'s <c>rel</c> is a set of tokens separated by ASCII whitespace (HTML §4.6.1, §2.3.8):
/// a tab, line feed, form feed or carriage return separates <c>stylesheet</c> from its neighbours as a space
/// does.
/// </summary>
/// <remarks>The stylesheet collection split on the space character only, so these sheets were never loaded.</remarks>
public sealed class LinkRelTokenTests
{
    private const string Sheet = "data:text/css,%23box%20%7B%20width:%2042px%20%7D";

    [Theory]
    [InlineData("stylesheet")]
    [InlineData("STYLESHEET")]
    [InlineData("preload stylesheet")]
    [InlineData("preload\nstylesheet")]
    [InlineData("preload\tstylesheet")]
    [InlineData("\r\nstylesheet\f")]
    public void A_Stylesheet_Token_Loads_The_Sheet(string rel)
    {
        Assert.Equal(42, Width(rel), 0.5);
    }

    [Theory]
    [InlineData("preload")]
    [InlineData("stylesheets")]
    [InlineData("style sheet")]
    public void Another_Token_Loads_Nothing(string rel)
    {
        Assert.NotEqual(42, Width(rel), 0.5);
    }

    private static float Width(string rel)
    {
        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            MaxSize = new SizeF(800, 600),
        };
        var encodedRel = System.Net.WebUtility.HtmlEncode(rel).Replace("\n", "&#10;").Replace("\t", "&#9;").Replace("\r", "&#13;").Replace("\f", "&#12;");
        container.SetHtmlWithStyleSet(
            $"<!DOCTYPE html><html><head><link rel=\"{encodedRel}\" href=\"{Sheet}\"></head>"
            + "<body style=\"margin: 0\"><div id=\"box\" style=\"height: 10px\"></div></body></html>",
            null,
            "about:blank");
        container.PerformLayout();
        return container.GetElementRectangle("box")!.Value.Width;
    }
}
