using System.Drawing;
using Broiler.Dom;
using Broiler.Dom.Html;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

/// <summary>
/// A table's <c>cellpadding</c> and <c>border</c> attributes reach its cells — every <c>&lt;td&gt;</c> and
/// <c>&lt;th&gt;</c> of the rows <c>HTMLTableElement.rows</c> lists, in a direct row or in a
/// <c>&lt;thead&gt;</c>, <c>&lt;tbody&gt;</c> or <c>&lt;tfoot&gt;</c> — and nothing else: not the caption's
/// content, not a nested table's cells.
/// </summary>
/// <remarks>
/// The hints used to be applied by counting box levels below the table. A <c>&lt;th&gt;</c> in a direct row
/// (as a host-built DOM supplies it, without the <c>&lt;tbody&gt;</c> the parser inserts) had its children
/// padded instead of itself, and the caption's content was padded as if it were a cell.
/// </remarks>
public sealed class TableCellHintTests
{
    [Fact]
    public void Cellpadding_Reaches_Every_Cell_Of_Every_Section()
    {
        var tree = Layout(
            "<table cellpadding=\"7\"><caption><div><div>caption-marker</div></div></caption>"
            + "<thead><tr><th>head-marker</th></tr></thead>"
            + "<tbody><tr><td>body-marker</td><th>side-marker</th></tr></tbody>"
            + "<tfoot><tr><td>foot-marker</td></tr></tfoot></table>");

        foreach (var (tag, marker) in new[] { ("th", "head-marker"), ("td", "body-marker"), ("th", "side-marker"), ("td", "foot-marker") })
            Assert.Equal(7, Find(tree, tag, marker).Padding.Top, 0.5);

        Assert.Equal(0, Find(tree, "div", "caption-marker", innermost: true).Padding.Left, 0.5);
    }

    [Fact]
    public void Cellpadding_Does_Not_Reach_A_Nested_Tables_Cells()
    {
        var tree = Layout(
            "<table cellpadding=\"7\"><tr><td>outer-marker<table><tr><td>inner-marker</td></tr></table></td></tr></table>");

        Assert.Equal(7, Find(tree, "td", "outer-marker").Padding.Top, 0.5);
        Assert.NotEqual(7, Find(tree, "td", "inner-marker", innermost: true).Padding.Top, 0.5);
    }

    [Fact]
    public void An_Author_Declaration_Still_Overrides_The_Cellpadding_Hint()
    {
        var tree = Layout("<style>td { padding: 2px }</style><table cellpadding=\"7\"><tr><td>cell-marker</td></tr></table>");

        Assert.Equal(2, Find(tree, "td", "cell-marker").Padding.Top, 0.5);
    }

    [Fact]
    public void The_Border_Attribute_Borders_The_Cells_And_Not_The_Caption()
    {
        var tree = Layout(
            "<table border=\"1\"><caption><div><div>caption-marker</div></div></caption><tr><th>head-marker</th><td>cell-marker</td></tr></table>");

        Assert.Equal(1, Find(tree, "th", "head-marker").Border.Top, 0.5);
        Assert.Equal(1, Find(tree, "td", "cell-marker").Border.Top, 0.5);
        Assert.Equal(0, Find(tree, "div", "caption-marker", innermost: true).Border.Top, 0.5);
    }

    /// <summary>
    /// A host-built table with rows directly under it: a header cell there is a cell, padded itself.
    /// </summary>
    [Fact]
    public void A_Direct_Rows_Header_Cell_In_A_Host_Built_Table_Is_Padded()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><html><body style=\"margin: 0\"></body></html>").Document;
        var table = document.CreateElement("table");
        table.SetAttribute("cellpadding", "7");
        var row = document.CreateElement("tr");
        table.AppendChild(row);
        foreach (var (tag, marker) in new[] { ("th", "head-marker"), ("td", "cell-marker") })
        {
            var cell = document.CreateElement(tag);
            cell.AppendChild(document.CreateTextNode(marker));
            row.AppendChild(cell);
        }
        document.Body!.AppendChild(table);

        using var container = Container();
        container.SetDocumentWithStyleSet(document, null, "about:blank");
        container.PerformLayout();
        var tree = container.LatestFragmentTree ?? throw new InvalidOperationException("No fragment tree.");

        Assert.Equal(7, Find(tree, "th", "head-marker").Padding.Top, 0.5);
        Assert.Equal(7, Find(tree, "td", "cell-marker").Padding.Top, 0.5);
    }

    private static HtmlContainer Container() => new()
    {
        AvoidAsyncImagesLoading = true,
        AvoidImagesLateLoading = true,
        MaxSize = new SizeF(800, 600),
    };

    private static Fragment Layout(string body)
    {
        using var container = Container();
        container.SetHtmlWithStyleSet("<!DOCTYPE html><html><body style=\"margin: 0\">" + body + "</body></html>", null, "about:blank");
        container.PerformLayout();
        return container.LatestFragmentTree ?? throw new InvalidOperationException("No fragment tree.");
    }

    /// <summary>
    /// The box of <paramref name="tag"/> whose text holds <paramref name="marker"/>: the outermost one, or
    /// with <paramref name="innermost"/> the deepest.
    /// </summary>
    private static Fragment Find(Fragment tree, string tag, string marker, bool innermost = false)
    {
        var matches = Flatten(tree).Where(f => f.Style.TagName == tag && Text(f).Contains(marker, StringComparison.Ordinal));
        return innermost ? matches.Last() : matches.First();
    }

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
