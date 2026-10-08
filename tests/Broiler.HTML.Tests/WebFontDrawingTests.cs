using System.Buffers.Binary;
using System.Drawing;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.HTML.Graphics;
using Broiler.HTML.Image;

namespace Broiler.HTML.Tests;

/// <summary>
/// Text in an <c>@font-face</c> web font is drawn in that font's own glyphs, in the window's render
/// list as in the renderer's own raster, where CSS puts them.
/// </summary>
/// <remarks>
/// Layout measured a web font's advances, but the render list named only its family, and every
/// backend looked that up among installed fonts: the web font is installed nowhere, so another face
/// was drawn. Acid3's <c>map::after</c> is the plain case — a white "X" in an Ahem-like font whose
/// "X" is a full-em square, on a fuchsia box exactly one em square, so in a browser the box is solid
/// white. The window drew an installed "X" with the fuchsia round it, and both outputs laid the
/// glyph out in a 1.16em content area, which put it 2px above the box.
/// </remarks>
public sealed class WebFontDrawingTests
{
    private static readonly BColor Fuchsia = BColor.FromArgb(255, 255, 0, 255);

    private const int Width = 100;
    private const int Height = 60;

    private static string Page(string family, string fontUrl) => $$"""
        <!DOCTYPE html><html><head><style>
        @font-face { font-family: "{{family}}"; src: url({{fontUrl}}); }
        html, body { margin: 0; background: white; }
        map::after { position: absolute; top: 18px; left: 38px; content: "X"; background: fuchsia;
          color: white; font: 20px/1 {{family}}; }
        </style></head><body><map></map></body></html>
        """;

    private static LoopbackHttpServer ServeFont() => new(request => request.Path switch
    {
        "/font.ttf" => LoopbackResponse.Ok("application/x-truetype-font", SquareGlyphFont()),
        _ => LoopbackResponse.NotFound(),
    });

    private static HtmlContainer Load(LoopbackHttpServer server, string family)
    {
        var container = TestContent.Container();
        container.MaxSize = new SizeF(Width, Height);
        Assert.Empty(TestContent.Render(container, Page(family, server.Url("/font.ttf")), server.Url("/page")));
        return container;
    }

    private static string UniqueFamily() => "AcidAhemTest" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>The render list's run names the web font's family and carries its program.</summary>
    [Fact]
    public void The_Render_List_Carries_The_Web_Font_It_Measured()
    {
        using var server = ServeFont();
        string family = UniqueFamily();
        using var container = Load(server, family);
        using var renderer = new BImageRenderer();
        using var renderList = HtmlGraphicsRenderListBuilder.Build(
            renderer, container.CreateDisplayList(), new RectangleF(0, 0, Width, Height));

        var runs = renderList.RenderList.Commands.OfType<BRenderCommand.DrawText>()
            .Where(static command => command.Text.Text == "X").ToList();
        Assert.NotEmpty(runs);
        Assert.All(runs, run =>
        {
            Assert.Equal(family, run.Text.Font.FamilyName);
            var face = Assert.IsType<Broiler.Graphics.Text.BFontFace>(run.Text.Font.Face);
            Assert.Equal(20, face.MeasureAdvance("X", run.Text.Font.Size), 3);
        });
    }

    /// <summary>An installed family carries no program: the backend resolves it by name as before.</summary>
    [Fact]
    public void An_Installed_Family_Carries_No_Face()
    {
        using var container = TestContent.Container();
        container.MaxSize = new SizeF(Width, Height);
        Assert.Empty(TestContent.Render(container, "<p style=\"font-family: sans-serif\">Text</p>", "https://example.test/"));
        using var renderer = new BImageRenderer();
        using var renderList = HtmlGraphicsRenderListBuilder.Build(
            renderer, container.CreateDisplayList(), new RectangleF(0, 0, Width, Height));

        var runs = renderList.RenderList.Commands.OfType<BRenderCommand.DrawText>().ToList();
        Assert.NotEmpty(runs);
        Assert.All(runs, static run => Assert.Null(run.Text.Font.Face));
    }

    /// <summary>The window's path: the render list replayed by a backend fills the fuchsia box white.</summary>
    [Fact]
    public void The_Window_Draws_The_Web_Fonts_Glyph_Over_Its_Em_Box()
    {
        using var server = ServeFont();
        using var container = Load(server, UniqueFamily());
        using var renderer = new BImageRenderer();
        using var renderList = HtmlGraphicsRenderListBuilder.Build(
            renderer, container.CreateDisplayList(), new RectangleF(0, 0, Width, Height));
        using Broiler.Graphics.Imaging.BBitmap image = renderer.RenderToImage(
            renderList.RenderList,
            BSurfaceDescriptor.Default(new BSize(Width, Height)),
            new BFrameContext(BColor.White));

        AssertSolidWhiteBox(image);
    }

    /// <summary>The renderer's own raster (an image, the command line's screenshot) agrees.</summary>
    [Fact]
    public void The_Raster_Draws_The_Web_Fonts_Glyph_Over_Its_Em_Box()
    {
        using var server = ServeFont();
        using var container = Load(server, UniqueFamily());
        using var image = new BBitmap(Width, Height);
        image.Erase(BColor.White);
        var clip = new RectangleF(0, 0, Width, Height);
        container.PerformLayout(image, clip);
        container.PerformPaint(image, clip);

        AssertSolidWhiteBox(image);
    }

    private static void AssertSolidWhiteBox(Broiler.Graphics.Imaging.BBitmap image) =>
        AssertSolidWhiteBox(image.GetPixel);

    private static void AssertSolidWhiteBox(BBitmap image) => AssertSolidWhiteBox(image.GetPixel);

    private static void AssertSolidWhiteBox(Func<int, int, BColor> getPixel)
    {
        for (int y = 18; y < 38; y++)
        {
            for (int x = 38; x < 58; x++)
            {
                BColor pixel = getPixel(x, y);
                Assert.True(
                    (pixel.R, pixel.G, pixel.B) == (255, 255, 255),
                    $"({x},{y}) in the 20x20 box at (38,18) is {pixel}, not white.");
            }
        }
    }

    /// <summary>
    /// An Ahem-like TrueType font assembled byte by byte: 1000 units per em, ascender 800,
    /// descender -200, and one glyph, "X", a full-em square from the descender to the ascender with
    /// a full-em advance. No machine has it installed, and no installed "X" fills its em box.
    /// </summary>
    private static byte[] SquareGlyphFont()
    {
        var tables = new List<(string Tag, byte[] Data)>();

        byte[] head = new byte[54];
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(18), 1000);
        tables.Add(("head", head));

        byte[] hhea = new byte[36];
        BinaryPrimitives.WriteUInt32BigEndian(hhea.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(4), 800);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(6), -200);
        BinaryPrimitives.WriteUInt16BigEndian(hhea.AsSpan(34), 2);
        tables.Add(("hhea", hhea));

        byte[] maxp = new byte[6];
        BinaryPrimitives.WriteUInt32BigEndian(maxp.AsSpan(0), 0x00005000);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(4), 2);
        tables.Add(("maxp", maxp));

        byte[] hmtx = new byte[8];
        BinaryPrimitives.WriteUInt16BigEndian(hmtx.AsSpan(0), 1000);
        BinaryPrimitives.WriteUInt16BigEndian(hmtx.AsSpan(4), 1000);
        tables.Add(("hmtx", hmtx));

        // One (3,1) subtable, format 6, mapping 'X' to glyph 1.
        byte[] cmap = new byte[24];
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(8), 12);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(12), 6);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(14), 12);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(18), 'X');
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(22), 1);
        tables.Add(("cmap", cmap));

        // Glyph 1: one clockwise contour (0,-200) (0,800) (1000,800) (1000,-200), on-curve points,
        // 16-bit coordinate deltas.
        byte[] glyf = new byte[36];
        BinaryPrimitives.WriteInt16BigEndian(glyf.AsSpan(0), 1);
        BinaryPrimitives.WriteInt16BigEndian(glyf.AsSpan(4), -200);
        BinaryPrimitives.WriteInt16BigEndian(glyf.AsSpan(6), 1000);
        BinaryPrimitives.WriteInt16BigEndian(glyf.AsSpan(8), 800);
        BinaryPrimitives.WriteUInt16BigEndian(glyf.AsSpan(10), 3);
        for (int i = 0; i < 4; i++)
            glyf[14 + i] = 0x01;
        short[] deltas = [0, 0, 1000, 0, -200, 1000, 0, -1000];
        for (int i = 0; i < deltas.Length; i++)
            BinaryPrimitives.WriteInt16BigEndian(glyf.AsSpan(18 + (i * 2)), deltas[i]);
        tables.Add(("glyf", glyf));

        byte[] loca = new byte[6];
        BinaryPrimitives.WriteUInt16BigEndian(loca.AsSpan(4), (ushort)(glyf.Length / 2));
        tables.Add(("loca", loca));

        tables.Sort(static (left, right) => string.CompareOrdinal(left.Tag, right.Tag));

        int directory = 12 + (tables.Count * 16);
        int total = directory + tables.Sum(static table => (table.Data.Length + 3) & ~3);
        byte[] sfnt = new byte[total];
        BinaryPrimitives.WriteUInt32BigEndian(sfnt.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(sfnt.AsSpan(4), (ushort)tables.Count);

        int record = 12;
        int offset = directory;
        foreach ((string tag, byte[] data) in tables)
        {
            for (int i = 0; i < 4; i++)
                sfnt[record + i] = (byte)tag[i];
            BinaryPrimitives.WriteUInt32BigEndian(sfnt.AsSpan(record + 8), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(sfnt.AsSpan(record + 12), (uint)data.Length);
            data.CopyTo(sfnt.AsSpan(offset));
            record += 16;
            offset += (data.Length + 3) & ~3;
        }

        return sfnt;
    }
}
