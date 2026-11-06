using System.Drawing;
using Broiler.Graphics.Color;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

public sealed class SelectionPaintTests
{
    [Fact]
    public void PartialSelectionPaintsAndClearsWithoutAnotherLayout()
    {
        using var container = new HtmlContainer { MaxSize = new SizeF(600, 200) };
        container.SetHtmlWithStyleSet("<!doctype html><p style='font:24px monospace'>Selection</p>");
        container.PerformLayout();
        var word = container.CreateDisplayList().Items.OfType<DrawTextItem>().Single(item => item.Text == "Selection");
        float y = word.Bounds.Top + word.Bounds.Height / 2;
        var start = new PointF(word.Bounds.Left + word.Bounds.Width * .25f, y);
        var end = new PointF(word.Bounds.Left + word.Bounds.Width * .75f, y);

        Assert.True(container.HasSelectableTextAt(start));
        Assert.False(container.HasSelectableTextAt(new PointF(450, y)));

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

    private static IEnumerable<FillRectItem> Highlights(HtmlContainer container) =>
        container.CreateDisplayList().Items.OfType<FillRectItem>()
            .Where(item => item.Color == BColor.FromArgb(0x69, 0x33, 0x99, 0xFF));
}
