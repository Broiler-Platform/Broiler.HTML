using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Broiler.Graphics.Color;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Core.Utils;
using Broiler.HTML.Image;

namespace Broiler.HTML.Tests;

/// <summary>
/// <c>data:</c> URLs decode as a browser decodes them: web-platform-tests' own vectors for the
/// <c>data:</c> URL processor and its forgiving-base64 bodies (copied under <c>wpt/</c>), and the
/// image and stylesheet loaders that use it.
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

    private static readonly IReadOnlyList<Vector> Base64Vectors =
        LoadVectors("base64.json", static entry => Bytes(entry[1]) is { } body
            ? new Vector("data:;base64," + entry[0].GetString(), "text/plain", body)
            : new Vector("data:;base64," + entry[0].GetString(), null, null));

    private static readonly IReadOnlyList<Vector> DataUrlVectors =
        LoadVectors("data-urls.json", static entry => entry.Length > 2
            ? new Vector(entry[0].GetString()!, entry[1].GetString()!.Split(';')[0], Bytes(entry[2]))
            : new Vector(entry[0].GetString()!, null, null));

    /// <summary>
    /// The vectors the URL parser decides rather than the processor: this processor takes the URL as
    /// written, so their failures are the parser's (see <see cref="DataUrl"/>).
    /// </summary>
    private static readonly HashSet<string> ParserVectors = ["data://test:test/,X"];

    public static TheoryData<int> Base64VectorIndexes() => Indexes(Base64Vectors.Count);

    public static TheoryData<int> DataUrlVectorIndexes() => Indexes(DataUrlVectors.Count);

    [Theory]
    [MemberData(nameof(Base64VectorIndexes))]
    public void A_Base64_Body_Decodes_As_Wpt_Expects(int index) => AssertVector(Base64Vectors[index]);

    [Theory]
    [MemberData(nameof(DataUrlVectorIndexes))]
    public void A_Data_Url_Decodes_As_Wpt_Expects(int index)
    {
        var vector = DataUrlVectors[index];
        if (!ParserVectors.Contains(vector.Input))
            AssertVector(vector);
    }

    [Fact]
    public void The_Mime_Type_Is_The_Essence_Without_The_Base64_Marker()
    {
        Assert.True(DataUrl.TryParse("data:IMAGE/PNG;base64," + UnpaddedRedPng, out var mimeType, out _));
        Assert.Equal("image/png", mimeType);

        Assert.True(DataUrl.TryParse("data: text/html ;charset=utf-8,X", out mimeType, out _));
        Assert.Equal("text/html", mimeType);

        // No type, or one that does not parse, is text/plain.
        Assert.True(DataUrl.TryParse("data:;charset=x;base64,WA", out mimeType, out _));
        Assert.Equal("text/plain", mimeType);

        Assert.True(DataUrl.TryParse("data:image;base64,WA", out mimeType, out _));
        Assert.Equal("text/plain", mimeType);
    }

    [Fact]
    public void A_Text_Body_Is_Utf8_Without_Its_Byte_Order_Mark()
    {
        Assert.Equal("é", DataUrl.Utf8Decode([0xEF, 0xBB, 0xBF, 0xC3, 0xA9]));
        Assert.Equal("﻿A", DataUrl.Utf8Decode([0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF, (byte)'A']));
        Assert.Equal("�A", DataUrl.Utf8Decode([0xFF, (byte)'A']));
        Assert.Equal(string.Empty, DataUrl.Utf8Decode([]));
    }

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
            Assert.True(DataUrl.TryParse(url, out _, out var body));
            Assert.Equal(notAnImage, body);
            Assert.False(BBitmap.TryDecodeFrameAt(body, TimeSpan.Zero, out var bitmap));
            Assert.Null(bitmap);

            using var rendered = Render($"<html><body><img width=\"20\" height=\"20\" src=\"{url}\"></body></html>", errors);
        });

        Assert.Empty(thrown);
        var error = Assert.Single(errors);
        Assert.Equal(HtmlRenderErrorType.Image, error.Type);
        Assert.Null(error.Exception);
    }

    /// <summary>
    /// A WPT vector: the URL, and the essence of its MIME type and its body when it decodes, both
    /// <see langword="null"/> when it does not.
    /// </summary>
    private sealed record Vector(string Input, string? Essence, byte[]? Body);

    private static void AssertVector(Vector vector)
    {
        var decoded = DataUrl.TryParse(vector.Input, out var mimeType, out var body);
        var shown = JsonSerializer.Serialize(vector.Input);
        if (vector.Body is null)
        {
            Assert.False(decoded, $"{shown} should not decode");
            return;
        }

        Assert.True(decoded, $"{shown} should decode");
        Assert.Equal(vector.Essence, mimeType);
        Assert.Equal(vector.Body, body);
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

    private static IReadOnlyList<Vector> LoadVectors(string file, Func<JsonElement[], Vector> select)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "wpt", "fetch", "data-urls", "resources", file);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return [.. document.RootElement.EnumerateArray()
            .Where(static entry => entry.ValueKind == JsonValueKind.Array)
            .Select(entry => select([.. entry.EnumerateArray()]))];
    }

    private static byte[]? Bytes(JsonElement expected) =>
        expected.ValueKind == JsonValueKind.Null
            ? null
            : [.. expected.EnumerateArray().Select(static b => (byte)b.GetInt32())];

    private static TheoryData<int> Indexes(int count)
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < count; i++)
            data.Add(i);
        return data;
    }
}

/// <summary>Runs <see cref="DataUrlTests"/> alone: it counts exceptions thrown on every thread.</summary>
[CollectionDefinition(nameof(DataUrlTests), DisableParallelization = true)]
public sealed class DataUrlTestsCollection;
