using System.Drawing;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

/// <summary>
/// The legacy <c>align</c> and <c>valign</c> attributes, mapped as the HTML Standard's rendering
/// section maps them: as presentational hints, which a user-agent rule cannot replace and an author
/// rule still can.
/// </summary>
/// <remarks>
/// www.7-zip.org lays its page out in a table whose side columns are <c>&lt;TD valign="top"&gt;</c>
/// and <c>&lt;TD vAlign=top&gt;</c>. Both came out vertically centred, because the user-agent rule
/// <c>td, th { vertical-align: inherit }</c> replaced the attribute's <c>top</c> with the row's
/// <c>middle</c>.
/// </remarks>
public sealed class PresentationalAlignmentTests
{
    [Theory]
    [InlineData("<table><tr><td valign=\"top\">cell-marker</td></tr></table>", "top")]
    [InlineData("<table><tr><td VALIGN=TOP>cell-marker</td></tr></table>", "top")]
    [InlineData("<table><tr><td valign=\"bottom\">cell-marker</td></tr></table>", "bottom")]
    [InlineData("<table><tr valign=\"top\"><td>cell-marker</td></tr></table>", "top")]
    [InlineData("<table><tbody valign=\"bottom\"><tr><td>cell-marker</td></tr></tbody></table>", "bottom")]
    // An author rule on the row overrides the row's hint, and the cell inherits the author's value.
    [InlineData("<style>tr { vertical-align: bottom }</style><table><tr valign=\"top\"><td>cell-marker</td></tr></table>", "bottom")]
    // An author declaration still wins over the hint, from a style attribute or a style sheet.
    [InlineData("<table><tr><td valign=\"top\" style=\"vertical-align: bottom\">cell-marker</td></tr></table>", "bottom")]
    [InlineData("<style>td { vertical-align: bottom }</style><table><tr><td valign=\"top\">cell-marker</td></tr></table>", "bottom")]
    // Without an attribute the cell keeps the user-agent default, the row's middle.
    [InlineData("<table><tr><td>cell-marker</td></tr></table>", "middle")]
    public void A_Cell_Is_Aligned_Vertically_As_Its_Valign_Says(string body, string expected)
    {
        var cell = Find(Layout(body), "td", "cell-marker");

        Assert.Equal(expected, cell.Style.VerticalAlign);
    }

    /// <summary>A top-aligned cell's text starts at the top of a row a taller neighbour stretches.</summary>
    [Fact]
    public void A_Top_Aligned_Cell_Starts_At_The_Top_Of_Its_Row()
    {
        var tree = Layout(
            "<table cellspacing=0 cellpadding=0><tr>" +
            "<td VALIGN=TOP>topmarker</td>" +
            "<td>one<br>two<br>three<br>four<br>five<br>six</td>" +
            "</tr></table>");

        var cell = Find(tree, "td", "topmarker");
        var line = Assert.Single(
            Flatten(cell).SelectMany(static f => f.Lines ?? []),
            static l => l.Inlines.Any(static i => i.Text?.Contains("topmarker", StringComparison.Ordinal) == true));

        Assert.True(cell.Size.Height > 80, $"The row is {cell.Size.Height}px tall.");
        Assert.True(line.Y - cell.Location.Y < 5, $"The text starts {line.Y - cell.Location.Y}px below the top of its cell.");
    }

    [Theory]
    [InlineData("<table><tr><th align=\"left\">cell-marker</th></tr></table>", "th", "left")]
    [InlineData("<table><tr><td ALIGN=RIGHT>cell-marker</td></tr></table>", "td", "right")]
    [InlineData("<DIV ALIGN=CENTER>cell-marker</DIV>", "div", "center")]
    [InlineData("<table><tr><th align=\"left\" style=\"text-align: right\">cell-marker</th></tr></table>", "th", "right")]
    // Without an attribute a header cell keeps the user-agent default.
    [InlineData("<table><tr><th>cell-marker</th></tr></table>", "th", "center")]
    public void A_Horizontal_Align_Is_Text_Align(string body, string tag, string expected)
    {
        var box = Find(Layout(body), tag, "cell-marker");

        Assert.Equal(expected, box.Style.TextAlign);
    }

    /// <summary>
    /// A centred table's attribute moves the table, not its text: its cells are not centred with it.
    /// </summary>
    /// <remarks>
    /// The table's auto margins are what centre it, and Broiler.Layout does not yet resolve auto
    /// margins on a table — <c>margin: 0 auto</c> leaves one at the left edge too — so where the
    /// table lands is that component's to test.
    /// </remarks>
    [Fact]
    public void A_Centred_Tables_Cells_Are_Not_Centred()
    {
        var tree = Layout("<table align=\"center\" width=\"300\"><tr><td>cell-marker</td></tr></table>");

        var table = Find(tree, "table", "cell-marker");
        var cell = Find(tree, "td", "cell-marker");

        Assert.NotEqual("center", table.Style.TextAlign);
        Assert.NotEqual("center", cell.Style.TextAlign);
    }

    [Theory]
    [InlineData("<table align=\"right\" width=\"300\"><tr><td>cell-marker</td></tr></table>", "table", "right")]
    [InlineData("<table ALIGN=LEFT width=\"300\"><tr><td>cell-marker</td></tr></table>", "table", "left")]
    [InlineData("<p>cell-marker <img align=\"left\" width=\"40\" height=\"40\" src=\"data:,\"></p>", "img", "left")]
    [InlineData("<p>cell-marker <img align=\"right\" width=\"40\" height=\"40\" src=\"data:,\"></p>", "img", "right")]
    public void A_Left_Or_Right_Table_Or_Image_Floats(string body, string tag, string expected)
    {
        var tree = Layout(body);
        var box = Flatten(tree).First(f => f.Style.TagName == tag);

        Assert.Equal(expected, box.Style.Float);
    }

    private static Fragment Layout(string body)
    {
        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            MaxSize = new SizeF(800, 600),
        };
        container.SetHtmlWithStyleSet("<!DOCTYPE html><html><body style=\"margin: 0\">" + body + "</body></html>", null, "about:blank");
        container.PerformLayout();
        return container.LatestFragmentTree ?? throw new InvalidOperationException("No fragment tree.");
    }

    /// <summary>The first box of <paramref name="tag"/> whose own lines or descendants hold <paramref name="marker"/>.</summary>
    private static Fragment Find(Fragment tree, string tag, string marker) =>
        Flatten(tree).First(f => f.Style.TagName == tag && Text(f).Contains(marker, StringComparison.Ordinal));

    private static IEnumerable<Fragment> Flatten(Fragment fragment)
    {
        yield return fragment;
        foreach (var child in fragment.Children)
        {
            foreach (var descendant in Flatten(child))
                yield return descendant;
        }
    }

    private static string Text(Fragment fragment) =>
        string.Concat(Flatten(fragment).SelectMany(static f => f.Lines ?? []).SelectMany(static l => l.Inlines).Select(static i => i.Text));
}
