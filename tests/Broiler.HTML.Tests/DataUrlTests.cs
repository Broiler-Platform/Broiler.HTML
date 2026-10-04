using System.Runtime.ExceptionServices;
using System.Text;
using Broiler.Graphics.Color;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Image;
using Broiler.Net.Http;

namespace Broiler.HTML.Tests;

/// <summary>
/// The image and stylesheet loaders decode <c>data:</c> URLs as a browser decodes them, through
/// Broiler.Net's <see cref="DataUrl"/>, whose own tests run web-platform-tests' vectors.
/// </summary>
/// <remarks>
/// The loaders used <see cref="Convert.FromBase64String"/>, which throws for a body without its
/// <c>=</c> padding where a browser decodes it. reCAPTCHA's stylesheet has such a body, so every
/// render of its frame raised a <see cref="FormatException"/>, and an unpadded image or stylesheet
/// was dropped.
/// </remarks>
[Collection(nameof(DataUrlTests))]
public sealed class DataUrlTests
{
    /// <summary>A 1×1 opaque red PNG, without the <c>==</c> its encoder padded it with.</summary>
    private const string UnpaddedRedPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg";

    [Fact]
    public void An_Unpadded_Base64_Image_Is_Painted_Without_An_Error()
    {
        var errors = new List<HtmlRenderErrorEventArgs>();
        var html =
            "<html><body style=\"margin:0\">" +
            $"<img style=\"display:block\" width=\"20\" height=\"20\" src=\"data:image/png;base64,{UnpaddedRedPng}\">" +
            $"<div style=\"width:20px;height:20px;background-image:url(data:image/png;base64,{UnpaddedRedPng});background-size:20px 20px\"></div>" +
            "</body></html>";

        using var image = Render(html, errors);

        Assert.Empty(errors);
        AssertRed(image.GetPixel(10, 10));
        AssertRed(image.GetPixel(10, 30));
    }

    [Fact]
    public void An_Unpadded_Base64_Stylesheet_Applies()
    {
        var errors = new List<HtmlRenderErrorEventArgs>();
        var css = Convert.ToBase64String(Encoding.UTF8.GetBytes("#box{width:20px;height:20px;background:#ff0000}")).TrimEnd('=');
        Assert.NotEqual(0, css.Length % 4);

        using var image = Render(
            $"<html><head><link rel=\"stylesheet\" href=\"data:text/css;base64,{css}\"></head>" +
            "<body style=\"margin:0\"><div id=\"box\"></div></body></html>",
            errors);

        Assert.Empty(errors);
        AssertRed(image.GetPixel(10, 10));
    }

    /// <summary>
    /// A body that is not an image at all, shaped like reCAPTCHA's: a PNG signature with the wrong
    /// last byte. The base64 decodes, no codec takes the bytes, and the image is a failed load, as in
    /// a browser — reported without an exception on the way.
    /// </summary>
    [Fact]
    public void A_Data_Url_That_Holds_No_Image_Is_A_Failed_Load_Without_An_Exception()
    {
        byte[] notAnImage = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x00, 0x57, 0xD8, 0x44, 0x19, 0x40];
        var url = "data:image/png;base64," + Convert.ToBase64String(notAnImage).TrimEnd('=');

        var errors = new List<HtmlRenderErrorEventArgs>();
        var thrown = CaptureFirstChanceExceptions(() =>
        {
            Assert.True(DataUrl.TryParse(url, out var dataUrl));
            Assert.Equal(notAnImage, dataUrl.Body.ToArray());
            Assert.False(BBitmap.TryDecodeFrameAt(notAnImage, TimeSpan.Zero, out var bitmap));
            Assert.Null(bitmap);

            using var rendered = Render($"<html><body><img width=\"20\" height=\"20\" src=\"{url}\"></body></html>", errors);
        });

        Assert.Empty(thrown);
        var error = Assert.Single(errors);
        Assert.Equal(HtmlRenderErrorType.Image, error.Type);
        Assert.Null(error.Exception);
    }

    private static BBitmap Render(string html, List<HtmlRenderErrorEventArgs> errors)
    {
        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            MaxSize = new System.Drawing.SizeF(100, 100),
        };
        container.RenderError += (_, e) => errors.Add(e);
        container.SetHtmlWithStyleSet(html);

        var image = new BBitmap(100, 100);
        image.Erase(BColor.White);
        var clip = new System.Drawing.RectangleF(0, 0, 100, 100);
        container.PerformLayout(image, clip);
        container.PerformPaint(image, clip);
        return image;
    }

    private static void AssertRed(BColor pixel) => Assert.Equal((255, 0, 0), (pixel.R, pixel.G, pixel.B));

    /// <summary>
    /// The decoding exceptions — <see cref="FormatException"/> and <see cref="NotSupportedException"/>
    /// — thrown while <paramref name="action"/> ran, caught ones included, on any thread: a render
    /// loads its images on prefetch workers. The collection runs alone, so no other test's are counted.
    /// </summary>
    private static List<Exception> CaptureFirstChanceExceptions(Action action)
    {
        var thrown = new List<Exception>();
        void Record(object? sender, FirstChanceExceptionEventArgs e)
        {
            if (e.Exception is FormatException or NotSupportedException)
            {
                lock (thrown)
                    thrown.Add(e.Exception);
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += Record;
        try
        {
            action();
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= Record;
        }

        return thrown;
    }
}

/// <summary>Runs <see cref="DataUrlTests"/> alone: it counts exceptions thrown on every thread.</summary>
[CollectionDefinition(nameof(DataUrlTests), DisableParallelization = true)]
public sealed class DataUrlTestsCollection;
