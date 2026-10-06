using System.Drawing;
using Broiler.HTML.Image;

namespace Broiler.HTML.Tests;

/// <summary>
/// The link at a point is the one drawn there: an absolutely positioned link outside the box it is in, and a
/// link with an <c>id</c>, are found; an <c>a</c> without an <c>href</c> is no link.
/// </summary>
/// <remarks>
/// <para>
/// The search went into a box only for a point inside the box, but an absolutely positioned link stands where
/// its containing block puts it: one placed 40px down beside a paragraph whose margin collapsed through the
/// body lay below the body's box, and a click on it found nothing. And Layout took any <c>a</c> with an
/// <c>id</c> for a named anchor, which no click follows (Broiler.Layout's <c>CssBox.IsClickable</c>).
/// </para>
/// <para>Chromium draws the link 40px down and follows a click there.</para>
/// </remarks>
public sealed class LinkAtPointTests
{
    private const string PageUrl = "https://example.test/page";

    private static string? LinkAt(string body, float x, float y)
    {
        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            MaxSize = new SizeF(800, 600),
        };
        container.SetHtmlWithStyleSet("<!DOCTYPE html><html><body style=\"margin: 0\">" + body + "</body></html>", null, PageUrl);
        container.PerformLayout();
        return container.GetLinkAt(new PointF(x, y));
    }

    private const string Positioned = "position: absolute; left: 0; top: 40px; width: 80px; height: 30px; display: block";

    /// <summary>A link placed below the body's box by its <c>top</c> is found where it is drawn.</summary>
    [Fact]
    public void A_Positioned_Link_Outside_Its_Parent_Is_Found()
    {
        Assert.Equal("/next", LinkAt($"<a href=\"/next\" style=\"{Positioned}\">Next</a><p>text</p>", 20, 45));
    }

    /// <summary>A link with an <c>id</c> is a link.</summary>
    [Fact]
    public void A_Link_With_An_Id_Is_Found()
    {
        Assert.Equal("/next", LinkAt($"<a id=\"next\" href=\"/next\" style=\"{Positioned}\">Next</a>", 20, 45));
    }

    /// <summary>An <c>a</c> without an <c>href</c> is no link. It was one, with an empty target: the page's own URL.</summary>
    [Fact]
    public void An_A_Without_Href_Is_No_Link()
    {
        Assert.Null(LinkAt($"<a name=\"top\" style=\"{Positioned}\">Top</a>", 20, 45));
    }

    /// <summary>
    /// Controls, which pass before and after: no link where there is none, and none outside a box that clips
    /// what it holds -- a link there is hidden.
    /// </summary>
    [Fact]
    public void Control_No_Link_Where_None_Is_Drawn()
    {
        Assert.Null(LinkAt($"<a href=\"/next\" style=\"{Positioned}\">Next</a>", 200, 45));
        Assert.Null(LinkAt(
            "<div style=\"position: relative; overflow: hidden; width: 100px; height: 50px\">" +
            "<a href=\"/hidden\" style=\"position: absolute; left: 150px; top: 0; width: 80px; height: 30px; display: block\">Hidden</a></div>", 170, 10));
    }
}
