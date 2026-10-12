using System.Drawing;
using Broiler.HTML.Image;

namespace Broiler.HTML.Tests;

/// <summary>
/// <see cref="HtmlContainer.PageSize"/>, the page a print path lays a document out on: a page
/// height that paginates, and the continuous default that does not.
/// </summary>
public sealed class HtmlContainerPageSizeTests
{
    private const string TwoSections =
        "<html><body style=\"margin:0\">" +
        "<div id=\"first\" style=\"height:50px\">first</div>" +
        "<div id=\"second\" style=\"break-before:page;height:50px\">second</div>" +
        "</body></html>";

    [Fact]
    public void A_Forced_Break_Starts_The_Next_Page_When_The_Layout_Paginates()
    {
        using var container = new HtmlContainer { AvoidAsyncImagesLoading = true, AvoidImagesLateLoading = true };
        container.SetHtmlWithStyleSet(TwoSections);
        container.MaxSize = new SizeF(300, 200);
        container.PageSize = new SizeF(300, 200);

        container.PerformLayout(new RectangleF(0, 0, 300, 200));

        Assert.Equal(new SizeF(300, 200), container.PageSize);
        RectangleF? second = container.GetElementRectangle("second");
        Assert.NotNull(second);
        Assert.True(second!.Value.Top >= 199.5f, $"The broken-to section starts at {second.Value.Top}, not on the second page.");
    }

    [Fact]
    public void The_Default_Page_Is_Continuous_Media()
    {
        using var container = new HtmlContainer { AvoidAsyncImagesLoading = true, AvoidImagesLateLoading = true };
        container.SetHtmlWithStyleSet(TwoSections);
        container.MaxSize = new SizeF(300, 200);

        container.PerformLayout(new RectangleF(0, 0, 300, 200));

        Assert.Equal(new SizeF(99999, 99999), container.PageSize);
        Assert.Equal(50f, container.GetElementRectangle("second")!.Value.Top, 1);
    }

    [Fact]
    public void A_Leading_Heading_Prints_On_The_First_Page()
    {
        // CSS 2.1's sample print sheet breaks before every h1; no browser does, and with that rule a
        // page that opens with its title printed a blank first page.
        using IDisposable print = Broiler.CSS.Dom.CssPagedMedia.Pin(300, 200);
        using var container = new HtmlContainer { AvoidAsyncImagesLoading = true, AvoidImagesLateLoading = true };
        container.SetHtmlWithStyleSet("<!DOCTYPE html><html><body><h1 id=\"title\">Title</h1><p>body</p></body></html>");
        container.MaxSize = new SizeF(300, 200);
        container.PageSize = new SizeF(300, 200);

        container.PerformLayout(new RectangleF(0, 0, 300, 200));

        Assert.True(container.GetElementRectangle("title")!.Value.Top < 100, "The heading was pushed off the first page.");
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html><body>x</body></html>", false)]
    [InlineData("<html><body>x</body></html>", true)]
    public void The_Document_Mode_Survives_Where_The_Serialization_Drops_It(string html, bool quirks)
    {
        using var container = new HtmlContainer();
        container.SetHtmlWithStyleSet(html);

        Assert.Equal(quirks, container.IsQuirksMode);
        Assert.DoesNotContain("DOCTYPE", container.GetHtml(), StringComparison.OrdinalIgnoreCase);
    }
}
