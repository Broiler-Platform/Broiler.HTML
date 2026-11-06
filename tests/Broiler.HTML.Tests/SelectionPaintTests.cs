using System.Drawing;
using Broiler.Graphics.Color;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

public sealed class SelectionPaintTests
{
    [Theory]
    [InlineData("Selection", false, false)]
    [InlineData("<b><span>Selection</span></b>", true, false)]
    [InlineData("<b>Selection</b>", false, true)]
    [InlineData("Selection <b style='display:inline-block'><span>Adjacent</span></b>", false, false)]
    public void PartialSelectionPaintsAndClearsWithoutAnotherLayout(string markup, bool quickRetry, bool selectAllFirst)
    {
        using var container = new HtmlContainer { MaxSize = new SizeF(600, 200) };
        container.SetHtmlWithStyleSet($"<!doctype html><p style='font:24px monospace'>{markup}</p>");
        container.PerformLayout();
        var word = container.CreateDisplayList().Items.OfType<DrawTextItem>().Single(item => item.Text == "Selection");
        float y = word.Bounds.Top + word.Bounds.Height / 2;
        var start = new PointF(word.Bounds.Left + word.Bounds.Width * .25f, y);
        var end = new PointF(word.Bounds.Left + word.Bounds.Width * .75f, y);

        Assert.True(container.HasSelectableTextAt(start));
        Assert.False(container.HasSelectableTextAt(new PointF(450, y)));

        if (quickRetry)
        {
            var elsewhere = new PointF(450, y);
            container.HandleMouseDown(elsewhere);
            container.HandleMouseUp(elsewhere);
        }
        if (selectAllFirst)
            container.HandleKeyDown(controlKey: true, aKeyCode: true, cKeyCode: false);

        container.HandleMouseDown(start);
        container.HandleMouseMove(end, leftButton: true);
        container.HandleMouseUp(end);

        var selected = Assert.IsType<string>(container.GetSelectedText());
        Assert.InRange(selected.Length, 1, "Selection".Length - 1);
        var highlight = Assert.Single(Highlights(container));
        Assert.True(highlight.Bounds.Left > word.Bounds.Left);
        Assert.True(highlight.Bounds.Right < word.Bounds.Right);

        container.ClearSelectionOutside(new PointF(450, 100));
        Assert.Empty(Highlights(container));
    }

    [Fact]
    public void SelectAllPaintsWithoutAnotherLayout()
    {
        using var container = new HtmlContainer { MaxSize = new SizeF(600, 200) };
        container.SetHtmlWithStyleSet("<!doctype html><p>Selection</p>");
        container.PerformLayout();

        container.HandleKeyDown(controlKey: true, aKeyCode: true, cKeyCode: false);

        Assert.Equal("Selection", container.GetSelectedText());
        Assert.Single(Highlights(container));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TableSelectionFollowsCellOrderInEitherDirection(bool backwards, bool crossRows)
    {
        using var container = new HtmlContainer { MaxSize = new SizeF(600, 300) };
        container.SetHtmlWithStyleSet("""
            <!doctype html><body style="font:24px monospace">
            <table style="border-spacing:24px">
                <tr><td><p>First <b>Selection</b></p></td><td>Right</td></tr>
                <tr><td>Lower</td><td>Last</td></tr>
            </table>
            """);
        container.PerformLayout();

        var words = container.CreateDisplayList().Items.OfType<DrawTextItem>().ToList();
        PointF At(string text, float fraction)
        {
            var bounds = Assert.Single(words, word => word.Text == text).Bounds;
            return new PointF(bounds.Left + bounds.Width * fraction, bounds.Top + bounds.Height / 2);
        }

        var first = At(crossRows ? "Right" : "Selection", .25f);
        var last = At(crossRows ? "Lower" : "Right", .65f);
        var start = backwards ? last : first;
        var end = backwards ? first : last;
        container.HandleMouseDown(start);
        container.HandleMouseMove(end, leftButton: true);
        container.HandleMouseUp(end);

        var selected = Assert.IsType<string>(container.GetSelectedText());
        Assert.StartsWith(crossRows ? "ight" : "lection", selected);
        Assert.EndsWith(crossRows ? "Low" : "Rig", selected);
        Assert.Equal(2, Highlights(container).Count());
    }

    private static IEnumerable<FillRectItem> Highlights(HtmlContainer container) =>
        container.CreateDisplayList().Items.OfType<FillRectItem>()
            .Where(item => item.Color == BColor.FromArgb(0x69, 0x33, 0x99, 0xFF));
}
