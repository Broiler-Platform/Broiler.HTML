using System.Drawing;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.HTML.Graphics;
using Broiler.HTML.Image;

namespace Broiler.HTML.Tests;

/// <summary>
/// Two border sides of different colours meet along the diagonal of their corner (CSS Backgrounds 3
/// §4.4), in the window's render list as in the renderer's own raster.
/// </summary>
/// <remarks>
/// The render list drew each side as a rectangle the full length of its edge, so at a corner the
/// side drawn last covered the other. Acid2's nose is two boxes of nothing but borders: a black
/// bottom between two yellow sides, then a black top between two yellow sides, which join into a
/// black diamond. The window drew a black square beside a yellow one.
/// </remarks>
public sealed class BorderJoinTests
{
    private const int Size = 48;

    private static readonly BColor Yellow = BColor.FromArgb(255, 255, 255, 0);

    // Acid2's `.nose div div:before`: no content, no top, 12px sides, a 24×12 box at (12, 12).
    private const string NoseTop = """
        <!DOCTYPE html><html><head><style>
        html, body { margin: 0; background: white; }
        div { margin: 12px; width: 0; height: 0; border-style: none solid solid;
          border-color: red yellow black yellow; border-width: 12px; }
        </style></head><body><div></div></body></html>
        """;

    private static HtmlGraphicsRenderList Build(BImageRenderer renderer, string page)
    {
        using var container = TestContent.Container();
        container.MaxSize = new SizeF(Size, Size);
        Assert.Empty(TestContent.Render(container, page, "https://example.test/"));
        return HtmlGraphicsRenderListBuilder.Build(renderer, container.CreateDisplayList(), new RectangleF(0, 0, Size, Size));
    }

    /// <summary>
    /// The black bottom reaches up to a point between the yellow sides: below each corner's diagonal
    /// it is black, above it yellow.
    /// </summary>
    [Theory]
    [InlineData(22, 22, true)]  // bottom-left corner, below the diagonal
    [InlineData(14, 14, false)] // bottom-left corner, above it
    [InlineData(25, 22, true)]  // bottom-right corner, below the diagonal
    [InlineData(34, 14, false)] // bottom-right corner, above it
    public void Sides_Of_Different_Colours_Meet_On_The_Diagonal(int x, int y, bool black)
    {
        using var renderer = new BImageRenderer();
        using var renderList = Build(renderer, NoseTop);
        using Broiler.Graphics.Imaging.BBitmap image = renderer.RenderToImage(
            renderList.RenderList,
            BSurfaceDescriptor.Default(new BSize(Size, Size)),
            new BFrameContext(BColor.White));

        Assert.Equal(black ? BColor.Black : Yellow, image.GetPixel(x, y));
    }

    /// <summary>A border of one colour keeps its four rectangles: no corner needs a triangle.</summary>
    [Fact]
    public void Sides_Of_One_Colour_Need_No_Triangles()
    {
        using var renderer = new BImageRenderer();
        using var renderList = Build(renderer, NoseTop.Replace("red yellow black yellow", "black"));

        Assert.Empty(renderList.RenderList.Commands.OfType<BRenderCommand.FillTriangle>());
        Assert.NotEmpty(renderList.RenderList.Commands.OfType<BRenderCommand.FillRect>());
    }
}
