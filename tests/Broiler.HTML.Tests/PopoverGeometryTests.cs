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

    private static Fragment Layout(string body, bool placesAnchoredBoxes = false)
    {
        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            MaxSize = new SizeF(800, 600),
            PlacesAnchoredBoxes = placesAnchoredBoxes,
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

    /// <summary>
    /// A dialog's border is Chromium's <c>border: solid</c>, medium (3px), in the dialog's own text colour,
    /// which is black even in a red block. It was 1px, and the text took the block's red. (In a red
    /// <c>p</c> it would prove nothing: a <c>dialog</c> start tag closes an open <c>p</c>.)
    /// </summary>
    [Fact]
    public void A_Dialog_Has_A_Medium_Border_And_Its_Own_Black_Text()
    {
        var dialog = Find(Layout("<div style=\"color: red\">red <dialog open>colouredmarker</dialog></div>"), "colouredmarker");

        Assert.Equal(3, dialog.Border.Top, 1);
        Assert.Equal(3, dialog.Border.Left, 1);
        Assert.Equal("solid", dialog.Style.BorderTopStyle);
        Assert.Equal((0, 0, 0), (dialog.Style.ActualColor.R, dialog.Style.ActualColor.G, dialog.Style.ActualColor.B));
        Assert.Equal((0, 0, 0), (dialog.Style.ActualBorderTopColor.R, dialog.Style.ActualBorderTopColor.G, dialog.Style.ActualBorderTopColor.B));
    }

    /// <summary>
    /// A modal dialog in the top layer is centred in the viewport and its <c>::backdrop</c> covers the viewport, even
    /// inside a transformed container of definite size, which is the containing block of an ordinary fixed box
    /// (Chromium, measured). Layout took that container for the dialog's too: the dialog
    /// sat in the 200x20 container and the backdrop was its size.
    /// </summary>
    [Fact]
    public void A_Top_Layer_Dialog_Escapes_A_Transformed_Container()
    {
        var tree = Layout(
            "<div style=\"overflow: hidden; height: 20px; width: 200px; transform: translateX(10px)\">" +
            "<dialog open data-broiler-state=\"modal\" data-broiler-top-layer=\"1\" data-broiler-backdrop=\"rgba(0, 0, 0, 0.1)\">" +
            "toplayermarker</dialog></div>");

        var dialog = Find(tree, "toplayermarker");
        Assert.Equal((800 - dialog.Size.Width) / 2, dialog.Location.X, 1);
        Assert.Equal((600 - dialog.Size.Height) / 2, dialog.Location.Y, 1);

        var backdrop = Flatten(tree).Single(f => f.Style.Position == "fixed" && f.TopLayerOrder is not null && Text(f).Length == 0);
        Assert.Equal(new SizeF(800, 600), backdrop.Size);
    }

    /// <summary>
    /// With <see cref="HtmlContainer.PlacesAnchoredBoxes"/>, a popover with <c>position-anchor: --a; position-area:
    /// bottom</c> is placed under its anchor and centred on it, as Chromium places it (measured: (100.15, 130) under
    /// an 80x30 anchor at (100, 100)); without, as though it had no anchor --
    /// HTML's rules centre it in the viewport. The window never had the layout engine place anchored boxes: the
    /// switch is internal to Broiler.Layout, and its scripting host left these boxes to it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_Anchored_Popover_Is_Placed_When_The_Container_Places_Anchored_Boxes(bool places)
    {
        var tree = Layout(
            "<style>#anc { position: absolute; left: 100px; top: 100px; width: 80px; height: 30px; anchor-name: --a }" +
            "#p { position-anchor: --a; position-area: bottom; margin: 0 }</style>" +
            $"<div id=\"anc\">anchor</div><div id=\"p\" {Shown}>anchoredmarker</div>",
            places);

        var popover = Find(tree, "anchoredmarker");
        if (places)
        {
            Assert.Equal(130, popover.Location.Y, 1);
            Assert.Equal(140 - popover.Size.Width / 2, popover.Location.X, 1);
        }
        else
        {
            Assert.Equal(0, popover.Location.Y, 1);
            Assert.Equal(0, popover.Location.X, 1);
        }
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
