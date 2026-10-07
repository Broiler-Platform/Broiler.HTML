using System.Drawing;
using Broiler.Dom;
using Broiler.Graphics.Color;
using Broiler.HTML.Image;
using Broiler.Media.Image;

namespace Broiler.HTML.Tests;

/// <summary>
/// A container that is only measured (<see cref="HtmlContainer.ImageSizesOnly"/>) lays an image out at
/// the size its header gives, as a container that decodes it does, and decodes none.
/// </summary>
/// <remarks>
/// The Broiler browser's script bridge asks one container for the page's geometry and paints with
/// another, and the first decoded every image, though layout reads only an image's size. reCAPTCHA's
/// image challenge shows one picture in each of its tiles, so a click on a tile decoded it a tile at a
/// time, every time the click changed the page: 0.7 to 1.5 seconds of the click.
/// </remarks>
public sealed class ImageSizesOnlyTests
{
    private const string PageUrl = "https://example.test/page";

    /// <summary>A PNG, a JPEG and a GIF are as large in a container that reads their sizes as in one that decodes them.</summary>
    [Theory]
    [InlineData(ImageEncodeFormat.Png, 40, 30)]
    [InlineData(ImageEncodeFormat.Jpeg, 50, 20)]
    [InlineData(ImageEncodeFormat.Gif, 10, 60)]
    public void An_Image_Is_As_Large_As_When_It_Is_Decoded(ImageEncodeFormat format, int width, int height)
    {
        var source = DataUrl(Encode(format, width, height), format);

        var measured = ImageBox(source, sizesOnly: true);
        var decoded = ImageBox(source, sizesOnly: false);

        Assert.Equal(new SizeF(width, height), measured.Size);
        Assert.Equal(decoded, measured);
    }

    /// <summary>
    /// The size comes from the header alone: a PNG cut off after its header is 40 by 30 to a container
    /// that reads sizes, where a container that decodes it has no image to measure.
    /// </summary>
    [Fact]
    public void The_Size_Comes_From_The_Header_Alone()
    {
        var png = Encode(ImageEncodeFormat.Png, 40, 30);
        var source = DataUrl(png.AsSpan(0, 33).ToArray(), ImageEncodeFormat.Png);

        Assert.Equal(new SizeF(40, 30), ImageBox(source, sizesOnly: true).Size);
        Assert.NotEqual(new SizeF(40, 30), ImageBox(source, sizesOnly: false).Size);
    }

    /// <summary>An SVG is loaded whole, as before: its size depends on more than a header.</summary>
    [Fact]
    public void An_Svg_Is_As_Large_As_When_It_Is_Decoded()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"12\"><rect width=\"24\" height=\"12\" fill=\"red\"/></svg>";
        var source = "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));

        Assert.Equal(new SizeF(24, 12), ImageBox(source, sizesOnly: true).Size);
    }

    private static byte[] Encode(ImageEncodeFormat format, int width, int height)
    {
        using var bitmap = new BBitmap(width, height);
        bitmap.Clear(BColor.FromArgb(255, 200, 40, 40));
        return bitmap.Encode(format, 90);
    }

    private static string DataUrl(byte[] data, ImageEncodeFormat format) =>
        $"data:image/{format.ToString().ToLowerInvariant()};base64,{Convert.ToBase64String(data)}";

    /// <summary>The border box of the page's one <c>img</c>, from a headless layout of a container that reads or decodes images.</summary>
    private static RectangleF ImageBox(string source, bool sizesOnly)
    {
        var document = Broiler.Dom.Html.HtmlDocumentParser.ParseDocument(
            $"<!DOCTYPE html><html><body style=\"margin: 0\"><img src=\"{source}\"></body></html>").Document;
        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            ImageSizesOnly = sizesOnly,
        };
        container.SetDocumentWithStyleSet(document, baseUrl: PageUrl);
        var geometry = container.GetLayoutGeometry(new SizeF(800, 600));
        var image = document.Descendants().OfType<DomElement>().Single(static element => element.LocalName == "img");
        return geometry.TryGetValue(image, out var box) ? box.BorderBox : RectangleF.Empty;
    }
}
