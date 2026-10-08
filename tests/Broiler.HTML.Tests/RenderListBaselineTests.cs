using System.Drawing;
using Broiler.Graphics.Color;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.HTML.Graphics;
using Broiler.HTML.Image;
using Broiler.Layout.IR;

namespace Broiler.HTML.Tests;

/// <summary>
/// A run in the window's render list states the baseline layout put it on, so a backend draws its
/// glyphs standing on the line that images and inline blocks beside them stand on.
/// </summary>
/// <remarks>
/// The run carried only its top-left origin, and each backend hung the glyphs from it by its own
/// metrics: the managed renderer 0.8em down, where layout's baseline is 0.8 of the font's 1.16em
/// height down, 0.928em. Acid3's 100px score was drawn 13px above the line it was laid out on.
/// </remarks>
public sealed class RenderListBaselineTests
{
    private static readonly BColor Red = BColor.FromArgb(255, 255, 0, 0);

    [Fact]
    public void A_Run_States_The_Baseline_An_Inline_Block_Beside_It_Stands_On()
    {
        using var container = TestContent.Container();
        container.MaxSize = new SizeF(600, 300);
        Assert.Empty(TestContent.Render(container, Page, "https://example.test/"));

        DisplayList displayList = container.CreateDisplayList();
        var text = Assert.Single(displayList.Items.OfType<DrawTextItem>(), static item => item.Text == "H");
        var block = Assert.Single(displayList.Items.OfType<FillRectItem>(), static fill => fill.Color == Red);

        using var renderer = new BImageRenderer();
        using var renderList = HtmlGraphicsRenderListBuilder.Build(renderer, displayList, new RectangleF(0, 0, 600, 300));
        var run = Assert.Single(
            renderList.RenderList.Commands.OfType<BRenderCommand.DrawText>(),
            static command => command.Text.Text == "H");

        // An inline block with no lines stands on the baseline with its bottom edge.
        double? baseline = run.Text.Baseline;
        Assert.NotNull(baseline);
        Assert.Equal(block.Bounds.Bottom - text.Origin.Y, baseline.Value, 1);
        Assert.Equal(92.8, baseline.Value, 1);
    }

    /// <summary>
    /// The renderer's own raster stands an "H" on that line too: its foot lands on the inline
    /// block's bottom edge, where it used to stop the face's ascender below the run's top, 2px high
    /// at 100px.
    /// </summary>
    [Fact]
    public void The_Raster_Stands_Glyphs_On_The_Baseline_Layout_Computed()
    {
        using var container = TestContent.Container();
        container.MaxSize = new SizeF(600, 300);
        Assert.Empty(TestContent.Render(container, Page, "https://example.test/"));
        var block = Assert.Single(container.CreateDisplayList().Items.OfType<FillRectItem>(), static fill => fill.Color == Red);

        using var image = new BBitmap(600, 300);
        image.Erase(BColor.White);
        var clip = new RectangleF(0, 0, 600, 300);
        container.PerformLayout(image, clip);
        container.PerformPaint(image, clip);

        // The H's lowest inked row, in the columns left of the block.
        int foot = -1;
        for (int y = 0; y < 300 && foot < 0; y++)
        {
            for (int x = 0; x < (int)block.Bounds.Left - 2; x++)
            {
                if (image.GetPixel(x, 299 - y).R < 128)
                {
                    foot = 299 - y;
                    break;
                }
            }
        }

        Assert.InRange(foot + 1, block.Bounds.Bottom - 1, block.Bounds.Bottom + 1);
    }

    private const string Page =
        "<div style=\"font: bold 100px/120px sans-serif; margin: 0\">H" +
        "<span style=\"display: inline-block; width: 10px; height: 10px; background: red\"></span></div>";
}
