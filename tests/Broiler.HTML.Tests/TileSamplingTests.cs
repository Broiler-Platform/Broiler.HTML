using System.Drawing;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.HTML.Graphics;
using Broiler.HTML.Image;

namespace Broiler.HTML.Tests;

/// <summary>
/// A repeated background's tiles drawn at the image's own size ask to be sampled nearest-neighbour;
/// a tile the page scales stays smooth.
/// </summary>
/// <remarks>
/// Acid2 paints the yellow behind its eyes with two layers of a 2×2 checkerboard, half yellow and
/// half transparent, the second offset a pixel from the first, so together they are solid. Blended
/// at the Windows window's 150% scale, each tile's pixels let the red behind them through as an
/// orange dither.
/// </remarks>
public sealed class TileSamplingTests
{
    // Acid2's eye checkerboard: 2×2, yellow at the top left and bottom right, transparent elsewhere.
    private const string Checkerboard =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAABnRSTlMAAAAAAABupgeRAAAABmJLR0QA%2FwD%2FAP%2BgvaeTAAAAEUlEQVR42mP4%2F58BCv7%2FZwAAHfAD%2FabwPj4AAAAASUVORK5CYII%3D";

    private static List<BRenderCommand.DrawImage> Images(string style)
    {
        using var container = TestContent.Container();
        container.MaxSize = new SizeF(40, 40);
        string page = $$"""
            <!DOCTYPE html><html><head><style>
            html, body { margin: 0; }
            div { width: 24px; height: 12px; background: url({{Checkerboard}}); {{style}} }
            </style></head><body><div></div></body></html>
            """;
        Assert.Empty(TestContent.Render(container, page, "https://example.test/"));

        using var renderer = new BImageRenderer();
        using var renderList = HtmlGraphicsRenderListBuilder.Build(
            renderer, container.CreateDisplayList(), new RectangleF(0, 0, 40, 40));
        return renderList.RenderList.Commands.OfType<BRenderCommand.DrawImage>().ToList();
    }

    [Fact]
    public void Tiles_At_The_Images_Own_Size_Are_Sampled_Nearest_Neighbour()
    {
        var tiles = Images("");

        Assert.NotEmpty(tiles);
        Assert.All(tiles, tile => Assert.Equal(BImageSampling.NearestNeighbor, tile.Sampling));
    }

    [Fact]
    public void Tiles_The_Page_Scales_Stay_Smooth()
    {
        var tiles = Images("background-size: 6px 6px;");

        Assert.NotEmpty(tiles);
        Assert.All(tiles, tile => Assert.Equal(BImageSampling.Linear, tile.Sampling));
    }
}
