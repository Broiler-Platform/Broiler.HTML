using System.Drawing;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

/// <summary>
/// Where a showing popover and an open dialog are drawn: HTML's rendering rules, as Chromium's sheet has them,
/// make a popover a fixed box centred in the viewport and sized to its content, with a border and padding of its
/// own, an open dialog an absolute box centred across where it stands, and a modal one a fixed box centred in the
/// viewport. A popover was drawn at the viewport's top-left corner, and a dialog was a block in the flow.
/// </summary>
/// <remarks>Measured in Chromium; a scripting host stamps the showing state.</remarks>
public sealed class PopoverGeometryTests
{
    private const string PageUrl = "https://example.test/page";
    private const string Shown = "popover data-broiler-state=\"popover-open\"";

    private static Fragment Layout(string body)
    {
        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            MaxSize = new SizeF(800, 600),
        };
        container.SetHtmlWithStyleSet("<!DOCTYPE html><html><body>" + body + "</body></html>", null, PageUrl);
        container.PerformLayout();
        return container.LatestFragmentTree ?? throw new InvalidOperationException("No fragment tree.");
    }

    /// <summary>A popover is centred in the viewport, as wide and tall as its content, in a medium solid border with 0.25em of padding.</summary>
    [Fact]
    public void A_Showing_Popover_Is_Centred_And_Sized_To_Its_Content()
    {
        var popover = Find(Layout($"<div {Shown}>centredmarker</div>"), "centredmarker");

        Assert.Equal(3, popover.Border.Top, 1);
        Assert.Equal(4, popover.Padding.Left, 1);
        Assert.True(popover.Size.Width < 200, $"The popover is {popover.Size.Width}px wide: it should fit its content.");
        Assert.Equal((800 - popover.Size.Width) / 2, popover.Location.X, 1);
        Assert.Equal((600 - popover.Size.Height) / 2, popover.Location.Y, 1);
    }

    /// <summary>
    /// An author's <c>top</c> and <c>left</c> alone leave the other insets 0 and the margins auto: the popover is
    /// centred in what remains (Chromium: left 20 + half the rest). With the insets reset it is where they say.
    /// </summary>
    [Fact]
    public void Author_Insets_Combine_With_The_User_Agents()
    {
        var tree = Layout(
            "<style>#a { top: 50px; left: 20px } #b { inset: auto; top: 50px; left: 20px } #c { margin: 0 }</style>" +
            $"<div id=\"a\" {Shown}>offsetmarker</div><div id=\"b\" {Shown}>placedmarker</div><div id=\"c\" {Shown}>cornermarker</div>");

        var offset = Find(tree, "offsetmarker");
        Assert.Equal(20 + (780 - offset.Size.Width) / 2, offset.Location.X, 1);
        Assert.Equal(50 + (550 - offset.Size.Height) / 2, offset.Location.Y, 1);

        var placed = Find(tree, "placedmarker");
        Assert.Equal(20, placed.Location.X, 1);
        Assert.Equal(50, placed.Location.Y, 1);

        var corner = Find(tree, "cornermarker");
        Assert.Equal(0, corner.Location.X, 1);
        Assert.Equal(0, corner.Location.Y, 1);
    }

    /// <summary>A dialog showing as a popover has no <c>open</c> attribute, yet it is displayed, centred like any popover.</summary>
    [Fact]
    public void A_Dialog_Showing_As_A_Popover_Is_Displayed()
    {
        var dialog = Find(Layout($"<dialog {Shown}>dialogmarker</dialog>"), "dialogmarker");

        Assert.Equal(4, dialog.Padding.Top, 1);
        Assert.Equal((800 - dialog.Size.Width) / 2, dialog.Location.X, 1);
    }

    /// <summary>
    /// An open dialog is absolutely positioned where it stands in the flow -- the paragraph after it starts there
    /// too -- centred across and as wide as its content. It was a block as wide as the page, which pushed the
    /// paragraph down.
    /// </summary>
    [Fact]
    public void An_Open_Dialog_Is_Centred_Across_Where_It_Stands()
    {
        var tree = Layout("<p>beforemarker</p><dialog open>dialogmarker</dialog><p>aftermarker</p>");

        var dialog = Find(tree, "dialogmarker");
        var after = Flatten(tree).Last(f => f.Style.TagName == "p" && Text(f).Contains("aftermarker", StringComparison.Ordinal));
        Assert.Equal("absolute", dialog.Style.Position);
        Assert.Equal((800 - dialog.Size.Width) / 2, dialog.Location.X, 1);
        Assert.Equal(after.Location.Y, dialog.Location.Y, 1);
    }

    /// <summary>A modal dialog is fixed and centred in the viewport. It stood at the top, as wide as the page.</summary>
    [Fact]
    public void A_Modal_Dialog_Is_Centred_In_The_Viewport()
    {
        var dialog = Find(Layout("<dialog open data-broiler-state=\"modal\">modalmarker</dialog>"), "modalmarker");

        Assert.Equal("fixed", dialog.Style.Position);
        Assert.Equal((800 - dialog.Size.Width) / 2, dialog.Location.X, 1);
        Assert.Equal((600 - dialog.Size.Height) / 2, dialog.Location.Y, 1);
    }

    private static Fragment Find(Fragment tree, string marker) =>
        Flatten(tree).Last(f => f.Style.Position is "fixed" or "absolute" && Text(f).Contains(marker, StringComparison.Ordinal));

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
